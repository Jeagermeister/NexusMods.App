using System.Reactive.Disposables;
using DynamicData;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Downloads;
using NexusMods.Library.Tests.DownloadsService.Helpers;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.NexusModsApi;
using Xunit;
using SyncHelpers = NexusMods.Library.Tests.DownloadsService.Helpers.SynchronizationHelpers;

namespace NexusMods.Library.Tests.DownloadsService;

public class DownloadsServiceTests(
    IJobMonitor jobMonitor,
    Library.DownloadsService service,
    IServiceProvider serviceProvider)
{
    private readonly DownloadJobFactory _jobFactory = new(jobMonitor, serviceProvider);

    // Helper methods
    private CompositeDisposable SetupCollectionSubscriptions(
        out List<DownloadInfo> allDownloads,
        out List<DownloadInfo> completedDownloads,
        out List<DownloadInfo> activeDownloads,
        out List<DownloadInfo>? gameDownloads,
        GameId? gameId = null)
    {
        var disposables = new CompositeDisposable();
        
        // Create local lists first
        var localAllDownloads = new List<DownloadInfo>();
        var localCompletedDownloads = new List<DownloadInfo>();
        var localActiveDownloads = new List<DownloadInfo>();
        var localGameDownloads = gameId.HasValue ? new List<DownloadInfo>() : null;
        
        service.AllDownloads.Subscribe(changes => 
        {
            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                        localAllDownloads.Add(change.Current);
                        break;
                    case ChangeReason.Remove:
                        localAllDownloads.RemoveAll(d => d.Id == change.Key);
                        break;
                }
            }
        }).DisposeWith(disposables);
        
        service.CompletedDownloads.Subscribe(changes => 
        {
            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                        localCompletedDownloads.Add(change.Current);
                        break;
                    case ChangeReason.Remove:
                        localCompletedDownloads.RemoveAll(d => d.Id == change.Key);
                        break;
                }
            }
        }).DisposeWith(disposables);
        
        service.ActiveDownloads.Subscribe(changes => 
        {
            foreach (var change in changes)
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                        localActiveDownloads.Add(change.Current);
                        break;
                    case ChangeReason.Remove:
                        localActiveDownloads.RemoveAll(d => d.Id == change.Key);
                        break;
                }
            }
        }).DisposeWith(disposables);
        
        if (gameId.HasValue && localGameDownloads != null)
        {
            service.GetDownloadsForGame(gameId.Value).Subscribe(changes => 
            {
                foreach (var change in changes)
                {
                    switch (change.Reason)
                    {
                        case ChangeReason.Add:
                        case ChangeReason.Update:
                            localGameDownloads.Add(change.Current);
                            break;
                        case ChangeReason.Remove:
                            localGameDownloads.RemoveAll(d => d.Id == change.Key);
                            break;
                    }
                }
            }).DisposeWith(disposables);
        }
        
        // Assign to out parameters
        allDownloads = localAllDownloads;
        completedDownloads = localCompletedDownloads;
        activeDownloads = localActiveDownloads;
        gameDownloads = localGameDownloads;
        
        return disposables;
    }
    
    private CompositeDisposable SetupCollectionSubscriptions(
        out List<DownloadInfo> allDownloads,
        out List<DownloadInfo> completedDownloads,
        out List<DownloadInfo> activeDownloads)
    {
        return SetupCollectionSubscriptions(out allDownloads, out completedDownloads, out activeDownloads, out _);
    }
    
    [Fact]
    public async Task DirectDownloadJobs_SurfaceAlongsideWrapperJobs()
    {
        // A direct-style job (Thunderstore shape: the job IS the HTTP transfer, InnerJob = null)
        // must surface in the downloads service exactly like the wrapper-style Nexus job.
        // Uses its own service instance and tracks jobs by id, so it neither depends on nor
        // disturbs the shared singleton's cache state; ends by cancelling both jobs so the
        // shared job monitor is left clean for other tests.
        var connection = serviceProvider.GetRequiredService<NexusMods.MnemonicDB.Abstractions.IConnection>();
        using var ownService = new Library.DownloadsService(jobMonitor, connection);

        // Wrapper-style job (Nexus shape)
        var wrapperContext = _jobFactory.CreateAndStartDownloadJob(NexusModsGameId.From(1234u));
        wrapperContext.WaitForJobsReady(TimeSpan.FromSeconds(30)).Should().BeTrue();
        wrapperContext.SignalJobsToStart();

        // Direct-style job (Thunderstore shape)
        var directCompletion = new TaskCompletionSource<NexusMods.Paths.AbsolutePath>();
        var directReady = new ManualResetEventSlim();
        var directStart = new ManualResetEventSlim();
        var directJob = new TestDirectDownloadJob
        {
            Uri = new Uri("https://thunderstore.example/package.zip"),
            DownloadPageUri = new Uri("https://thunderstore.example/package/"),
            Destination = NexusMods.Paths.FileSystem.Shared.GetKnownPath(NexusMods.Paths.KnownPath.CurrentDirectory).Combine("test/downloads/DirectPackage.zip"),
            CompletionSource = directCompletion,
            DisplayName = "DirectPackage (TestTeam)",
            StartSignal = directStart,
            ReadySignal = directReady,
        };
        var directJobTask = jobMonitor.Begin<TestDirectDownloadJob, NexusMods.Paths.AbsolutePath>(directJob);
        directReady.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
        directStart.Set();

        var wrapperId = wrapperContext.JobTask.Job.Id;
        var directId = directJobTask.Job.Id;

        // Track by id via the service's own cache snapshot observable
        var tracked = new Dictionary<DownloadId, DownloadInfo>();
        using var subscription = ownService.AllDownloads.Subscribe(changes =>
        {
            foreach (var change in changes)
            {
                if (change.Reason is ChangeReason.Add or ChangeReason.Update or ChangeReason.Refresh)
                    tracked[change.Key] = change.Current;
                else if (change.Reason is ChangeReason.Remove)
                    tracked.Remove(change.Key);
            }
        });

        // Both jobs surface
        (await SyncHelpers.WaitUntil(() => tracked.ContainsKey(wrapperId) && tracked.ContainsKey(directId), TimeSpan.FromSeconds(30)))
            .Should().BeTrue("both wrapper-style and direct-style jobs should surface in AllDownloads");

        var directInfo = tracked[directId];
        directInfo.Name.Value.Should().Be("DirectPackage (TestTeam)", "direct jobs use their DisplayName");
        directInfo.GameId.Value.Should().Be(default(GameId), "direct jobs without a game association report the default game id");
        directInfo.DownloadPageUri.Value.Value.Should().Be(new Uri("https://thunderstore.example/package/"));

        // Pause/resume routes to the job itself when there is no inner job (InnerJob == null)
        ownService.PauseDownload(directInfo);
        (await SyncHelpers.WaitUntil(() => directInfo.Status.Value == JobStatus.Paused, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("pausing a direct job should pause the job itself");
        ownService.ResumeDownload(directInfo);
        (await SyncHelpers.WaitUntil(() => directInfo.Status.Value == JobStatus.Running, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("resuming a direct job should resume the job itself");

        // Cleanup: cancel both jobs so they are removed from the shared job monitor and caches.
        // The wrapper test job blocks on its completion source without a cancellation token,
        // so the sources must be cancelled too for the jobs to actually unwind.
        jobMonitor.Cancel(directJobTask);
        wrapperContext.CancelJob();
        wrapperContext.CompletionSource.TrySetCanceled();
        wrapperContext.HttpCompletionSource.TrySetCanceled();
        (await SyncHelpers.WaitUntil(() => !tracked.ContainsKey(wrapperId) && !tracked.ContainsKey(directId), TimeSpan.FromSeconds(30)))
            .Should().BeTrue("cancelled jobs should be removed");
    }

    [Fact]
    public async Task Validate_Download_Jobs_Lifetime()
    {
        // Arrange
        var gameId = NexusModsGameId.From(1234u);
        
        // Subscribe to collections - SourceCache publishes immediately on subscribe
        using var disposables = SetupCollectionSubscriptions(
            out var allDownloads,
            out var completedDownloads,
            out var activeDownloads,
            out var gameDownloads,
            GameId.From(gameId.Value));

        // 1. No jobs initially
        allDownloads.Should().BeEmpty("no jobs should exist initially");
        completedDownloads.Should().BeEmpty("no completed jobs should exist initially");
        activeDownloads.Should().BeEmpty("no active jobs should exist initially");
        gameDownloads!.Should().BeEmpty("no game-specific jobs should exist initially");
        
        // 2. Start job with signals for proper synchronization
        var context = _jobFactory.CreateAndStartDownloadJob(gameId);
        
        // Wait for jobs to signal they're ready before checking state
        context.WaitForJobsReady(TimeSpan.FromSeconds(30))
            .Should().BeTrue("jobs should signal ready within timeout");
        
        // Signal jobs to start and wait for collections to be updated
        context.SignalJobsToStart();
        
        // Wait for job to appear in collections with proper timeout
        (await SyncHelpers.WaitForCollectionCount(allDownloads, 1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in AllDownloads when started");
        (await SyncHelpers.WaitForCollectionCount(gameDownloads!, 1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in game-specific downloads when started");
        (await SyncHelpers.WaitForCollectionCount(activeDownloads, 1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in ActiveDownloads when started");
        completedDownloads.Should().BeEmpty("job should not be in CompletedDownloads when started");
        
        // 3. Complete job - should move to CompletedDownloads only
        context.CompleteJob();
        await context.JobTask.Job.WaitAsync();
        
        // Wait for completion to be processed by collections
        (await SyncHelpers.WaitForCollectionCount(completedDownloads, 1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("completed job should be in CompletedDownloads");
        (await SyncHelpers.WaitForCollectionCount(activeDownloads, 0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("completed job should not be in ActiveDownloads");
        
        // Verify final state
        allDownloads.Should().HaveCount(1, "completed job should remain in AllDownloads");
        completedDownloads.Should().HaveCount(1, "completed job should be in CompletedDownloads");
        activeDownloads.Should().BeEmpty("completed job should not be in ActiveDownloads");
        gameDownloads.Should().HaveCount(1, "completed job should remain in game-specific downloads");
    }
    
    [Fact]
    public async Task CancelledJobs_ShouldBeCompletelyRemoved_FromAllCollections()
    {
        // Arrange
        var gameId = NexusModsGameId.From(1234u);
        
        using var disposables = SetupCollectionSubscriptions(
            out var allDownloads,
            out var completedDownloads,
            out var activeDownloads);
        
        // Initially empty
        allDownloads.Should().BeEmpty("no jobs should exist initially");
        
        // Create and start job
        var context = _jobFactory.CreateAndStartDownloadJob(gameId);
        
        // Wait for jobs to signal they're ready before checking state
        context.WaitForJobsReady(TimeSpan.FromSeconds(30))
            .Should().BeTrue("jobs should signal ready within timeout");
        
        // Job should appear in collections
        allDownloads.Should().HaveCount(1, "job should be in AllDownloads when started");
        activeDownloads.Should().HaveCount(1, "job should be in ActiveDownloads when started");
        
        // Start a pre-Cancelled the job
        context.CancelJob();
        context.SignalJobsToStart();
        
        try
        {
            await context.JobTask.Job.WaitAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Cancelled jobs should be completely removed
        // Note: The change isn't instant so we must wait for the collections to update
        (await SyncHelpers.WaitForCollectionCount(allDownloads, 0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("cancelled jobs should be removed from AllDownloads");
        (await SyncHelpers.WaitForCollectionCount(completedDownloads, 0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("cancelled jobs should not be in CompletedDownloads");
        (await SyncHelpers.WaitForCollectionCount(activeDownloads, 0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("cancelled jobs should not be in ActiveDownloads");
    }

    // Nested Startup class for Xunit.DependencyInjection
    public class Startup
    {
        // https://github.com/pengweiqhca/Xunit.DependencyInjection?tab=readme-ov-file#3-closest-startup
        // A trick for parallelizing tests with Xunit.DependencyInjection
        public void ConfigureServices(IServiceCollection services) => DIHelpers.ConfigureServices(services);
    }
}
