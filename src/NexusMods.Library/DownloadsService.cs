using System.Diagnostics;
using System.Reactive.Disposables;
using DynamicData;
using DynamicData.Kernel;
using NexusMods.Abstractions.Downloads;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Networking.HttpDownloader;
using NexusMods.App.UI.Resources;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Library;
using R3;
using ReactiveUI;

namespace NexusMods.Library;

/// <summary>
/// Implementation of <see cref="IDownloadsService"/>.
/// </summary>
public sealed class DownloadsService : IDownloadsService, IDisposable
{
    private readonly IJobMonitor _jobMonitor;
    private readonly IConnection _connection;
    private readonly System.Reactive.Disposables.CompositeDisposable _disposables = new();
    
    private readonly SourceCache<DownloadInfo, DownloadId> _downloadCache = new(x => x.Id);
    
    /// <summary>
    /// Constructor.
    /// </summary>
    public DownloadsService(
        IJobMonitor jobMonitor,
        IConnection connection)
    {
        _jobMonitor = jobMonitor;
        _connection = connection;

        InitializeObservables();
    }

    private void InitializeObservables()
    {
        // TODO: Restore completed downloads from persistent storage on application boot.

        // Monitor library download jobs (any mod source) and transform them into DownloadInfo
        // Handle completed downloads by keeping them in cache when removed from JobMonitor
        _jobMonitor.GetObservableChangeSet<ILibraryDownloadJob>()
            .Subscribe(changes =>
            {
                _downloadCache.Edit(updater =>
                {
                    foreach (var change in changes)
                    {
                        switch (change.Reason)
                        {
                            case ChangeReason.Add:
                            case ChangeReason.Update:
                            case ChangeReason.Refresh:
                                var libraryJob = (ILibraryDownloadJob)change.Current.Definition;
                                // Wrapper jobs (Nexus) expose the inner HTTP job whose observables tick;
                                // jobs that ARE the HTTP transfer (Thunderstore) are observed directly.
                                var progressJob = libraryJob.InnerJob ?? change.Current;
                                var downloadInfo = CreateDownloadInfo(libraryJob, change.Current.Id);
                                updater.AddOrUpdate(downloadInfo, change.Current.Id);
                                
                                // Subscribe to job observables for reactive updates
                                if (change.Reason == ChangeReason.Add)
                                    SubscribeToJobObservables(progressJob, downloadInfo);

                                break;
                            case ChangeReason.Remove:
                                // Note(sewer): JobMonitor removes all jobs after completion, but we want to keep the completed jobs
                                //              to show in the 'Completed' tab.
                                //              Cancelled jobs can also yield a ChangeReason.Remove, so we need to make a distinction here. 
                                if (change.Current.Status == JobStatus.Completed)
                                {
                                    // Keep completed downloads but mark them as completed
                                    var existingItem = updater.Lookup(change.Key);
                                    if (existingItem.HasValue)
                                    {
                                        var completedDownload = existingItem.Value;
                                        MarkJobAsCompleted(completedDownload);
                                        // Clean up observable subscriptions
                                        completedDownload.Subscriptions?.Dispose();
                                    }
                                }
                                else
                                {
                                    // Remove non-completed downloads normally
                                    var existingItem = updater.Lookup(change.Key);
                                    if (existingItem.HasValue)
                                        existingItem.Value.Subscriptions?.Dispose();

                                    updater.RemoveKey(change.Key);
                                }
                                break;
                            case ChangeReason.Moved:
                                // Nothing to do for moves
                                break;
                        }
                    }
                });
            })
            .DisposeWith(_disposables);
    }
    
    // Observable properties implementation
    public IObservable<IChangeSet<DownloadInfo, DownloadId>> ActiveDownloads => 
        _downloadCache.Connect()
            .FilterOnObservable(x => x.Status.AsObservable().Select(status => status.IsActive()).AsSystemObservable())
            .RefCount();
    
    public IObservable<IChangeSet<DownloadInfo, DownloadId>> CompletedDownloads =>
        _downloadCache.Connect()
            .FilterOnObservable(x => x.Status.AsObservable().Select(status => status == JobStatus.Completed).AsSystemObservable())
            .RefCount();
    
    public IObservable<IChangeSet<DownloadInfo, DownloadId>> AllDownloads =>
        _downloadCache.Connect();
    
    public IObservable<IChangeSet<DownloadInfo, DownloadId>> GetDownloadsForGame(GameId gameId) =>
        _downloadCache.Connect()
            .Filter(x => x.GameId.Value.Equals(gameId));
    
    public IObservable<IChangeSet<DownloadInfo, DownloadId>> GetActiveDownloadsForGame(GameId gameId) =>
        _downloadCache.Connect()
            .FilterOnObservable(x => x.Status.AsObservable().Select(status => x.GameId.Value.Equals(gameId) && status.IsActive()).AsSystemObservable())
            .RefCount();
    
    /// <summary>
    /// Helper method to resolve a <see cref="DownloadInfo.Id"/> ID to the underlying <see cref="HttpDownloadJob"/> ID.
    /// This is a temporary workaround until the job system properly delegates capabilities 
    /// (tracked in issue #3892).
    /// </summary>
    /// <param name="downloadInfo">The download info containing the outer download job ID</param>
    /// <returns>The ID of the underlying HttpDownloadJob if found, otherwise the original ID</returns>
    private JobId ResolveToHttpDownloadJobId(DownloadInfo downloadInfo)
    {
        // Try to find the job in the job monitor
        var job = _jobMonitor.Find(downloadInfo.Id);
        if (job == null)
            return downloadInfo.Id;
        
        // Wrapper jobs expose the inner HTTP job that actually supports pause/resume.
        if (job.Definition is ILibraryDownloadJob { InnerJob: not null } libraryJob)
            return libraryJob.InnerJob.Id;
        
        return downloadInfo.Id;
    }
    
    // Control operations
    
    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void PauseDownload(DownloadInfo downloadInfo) => _jobMonitor.Pause(ResolveToHttpDownloadJobId(downloadInfo));
    
    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void ResumeDownload(DownloadInfo downloadInfo) => _jobMonitor.Resume(ResolveToHttpDownloadJobId(downloadInfo));
    
    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void CancelDownload(DownloadInfo downloadInfo) => _jobMonitor.Cancel(ResolveToHttpDownloadJobId(downloadInfo));
    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void PauseAll()
    {
        foreach (var download in _downloadCache.Items.Where(d => d.Status.Value == JobStatus.Running))
            _jobMonitor.Pause(ResolveToHttpDownloadJobId(download));
    }

    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void PauseAllForGame(GameId gameId)
    {
        foreach (var download in _downloadCache.Items.Where(d => 
            d.Status.Value == JobStatus.Running && d.GameId.Value.Equals(gameId)))
            _jobMonitor.Pause(ResolveToHttpDownloadJobId(download));
    }

    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void ResumeAll()
    {
        foreach (var download in _downloadCache.Items.Where(d => d.Status.Value == JobStatus.Paused))
            _jobMonitor.Resume(ResolveToHttpDownloadJobId(download));
    }

    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void ResumeAllForGame(GameId gameId)
    {
        foreach (var download in _downloadCache.Items.Where(d => 
            d.Status.Value == JobStatus.Paused && d.GameId.Value.Equals(gameId)))
            _jobMonitor.Resume(ResolveToHttpDownloadJobId(download));
    }
    
    // Note(sewer) Workaround for issue #3892: Resolve to the underlying HttpDownloadJob ID
    public void CancelRange(IEnumerable<DownloadInfo> downloads)
    {
        foreach (var download in downloads)
            _jobMonitor.Cancel(ResolveToHttpDownloadJobId(download));
    }
    
    private DownloadInfo CreateDownloadInfo(ILibraryDownloadJob libraryJob, JobId currentId)
    {
        var info = new DownloadInfo 
        { 
            Id = currentId,
        };
        
        // Set initial values using internal methods
        info.SetGameId(libraryJob.GameId);
        info.SetName(ExtractName(libraryJob));
        info.SetDownloadPageUri(libraryJob.DownloadPageUri);
        info.SetFileMetadataId(libraryJob.MetadataEntityId);
        info.LibraryFileResolver = libraryJob.FindLibraryFile;
        // FileSize, Progress, DownloadedBytes, TransferRate, Status, CompletedAt are set by observable subscriptions
        
        return info;
    }

    private void SubscribeToJobObservables(IJob httpDownloadJob, DownloadInfo downloadInfo)
    {
        // Ensure we don't have existing subscriptions for this job
        downloadInfo.Subscriptions?.Dispose();
        
        var jobDisposables = new System.Reactive.Disposables.CompositeDisposable();
        
        // Subscribe to progress changes
        httpDownloadJob.ObservableProgress
            .Subscribe(progress => downloadInfo.SetProgress(progress.HasValue ? progress.Value : Percent.Zero))
            .DisposeWith(jobDisposables);
        
        // Subscribe to rate of progress changes
        httpDownloadJob.ObservableRateOfProgress
            .Subscribe(rateOfProgress => downloadInfo.SetTransferRate(Size.FromLong((long)(rateOfProgress.HasValue ? rateOfProgress.Value : 0))))
            .DisposeWith(jobDisposables);
        
        // Subscribe to status changes
        httpDownloadJob.ObservableStatus
            .Subscribe(status => downloadInfo.SetStatus(status))
            .DisposeWith(jobDisposables);
        
        // Subscribe to reactive properties from IHttpDownloadState
        var state = httpDownloadJob.GetJobStateData<IHttpDownloadState>();
        Debug.Assert(state != null, "IHttpDownloadState should always exist for HttpDownloadJob");
        
        // Subscribe to ContentLength changes (FileSize)
        state.WhenAnyValue(x => x.ContentLength)
            .Subscribe(contentLength => downloadInfo.SetFileSize(contentLength.HasValue ? contentLength.Value : Size.From(0)))
            .DisposeWith(jobDisposables);

        // Subscribe to TotalBytesDownloaded changes (DownloadedBytes)
        state.WhenAnyValue(x => x.TotalBytesDownloaded)
            .Subscribe(totalBytes => downloadInfo.SetDownloadedBytes(totalBytes))
            .DisposeWith(jobDisposables);
        
        // Store the subscription for later disposal
        downloadInfo.Subscriptions = jobDisposables;
    }

    private static void MarkJobAsCompleted(DownloadInfo downloadInfo)
    {
        // Reset transient properties that are only relevant for active downloads
        downloadInfo.SetTransferRate(Size.From(0));
        
        // Set completion timestamp
        downloadInfo.SetCompletedAt(DateTimeOffset.UtcNow);
        
        // Keep Progress at 100% and other completed state
    }

    // Helper methods
    private string ExtractName(ILibraryDownloadJob libraryJob)
    {
        // Direct access to the source-provided display name
        var fileName = libraryJob.DisplayName;
        if (!string.IsNullOrEmpty(fileName))
            return fileName;
        
        // Note(sewer): The name should never be empty in practice, as we always fetch the metadata before
        // starting a download, however; as a precaution; we provide a fallback here.

        // Fallback to destination filename if the display name is empty as absolute last resort.
        if (libraryJob.Destination == default(AbsolutePath))
            return Language.Downloads_UnknownDownload;

        var destinationFileName = libraryJob.Destination.FileName;
        if (string.IsNullOrEmpty(destinationFileName))
            return Language.Downloads_UnknownDownload;
        
        var nameWithoutExt = Path.GetFileNameWithoutExtension(destinationFileName);
        return nameWithoutExt.Replace('_', ' ').Replace('-', ' ');
    }

    public Optional<LibraryFile.ReadOnly> ResolveLibraryFile(DownloadInfo downloadInfo)
    {
        // Only resolve for completed downloads
        if (downloadInfo.Status.Value != JobStatus.Completed)
            return Optional<LibraryFile.ReadOnly>.None;
        
        try
        {
            // Source-specific lookup captured from the originating download job.
            return downloadInfo.LibraryFileResolver?.Invoke(_connection.Db) ?? Optional<LibraryFile.ReadOnly>.None;
        }
        catch (Exception)
        {
            // Any database error results in None
            return Optional<LibraryFile.ReadOnly>.None;
        }
    }

    
    public void Dispose()
    {
        // Dispose all job subscriptions
        foreach (var downloadInfo in _downloadCache.Items)
            downloadInfo.Subscriptions?.Dispose();
        
        _disposables.Dispose();
        _downloadCache.Dispose();
    }
}
