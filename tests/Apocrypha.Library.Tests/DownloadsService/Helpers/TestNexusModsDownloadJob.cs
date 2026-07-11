using System.Reactive.Subjects;
using DynamicData.Kernel;
using Apocrypha.Abstractions.HttpDownloads;
using Apocrypha.Abstractions.NexusModsLibrary;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Library.Tests.DownloadsService.Helpers;

/// <summary>
/// Test implementation that implements <see cref="IJobDefinitionWithStart{TJobDefinition,TResult}"/>
/// This allows us to create real jobs that work with the real <see cref="IJobMonitor"/>
/// Note: Does not implement <see cref="INexusModsDownloadJob"/> due to type constraints with <see cref="TestHttpDownloadJob"/>
/// </summary>
public record TestNexusModsDownloadJob : IJobDefinitionWithStart<TestNexusModsDownloadJob, AbsolutePath>, INexusModsDownloadJob

{
    // Configuration properties - these come from the <see cref="IHttpDownloadJob"/> and FileMetadata
    public required NexusModsFileMetadata.ReadOnly FileMetadata { get; init; }
    
    // Control properties for testing
    public required BehaviorSubject<JobStatus> StatusController { get; init; }
    public required BehaviorSubject<double> ProgressController { get; init; }
    public required TaskCompletionSource<AbsolutePath> CompletionSource { get; init; }
    
    // Synchronization signals for deterministic testing
    public ManualResetEventSlim? StartSignal { get; init; }
    public ManualResetEventSlim? ReadySignal { get; init; }
    
    // <see cref="IDownloadJob"/> implementation
    public AbsolutePath Destination => HttpDownloadJob.JobDefinition.Destination;

    // Additional properties for test control
    public IJobTask<IHttpDownloadJob, AbsolutePath> HttpDownloadJob { get; set; } = null!;

    // <see cref="ILibraryDownloadJob"/> implementation
    public GameId GameId { get; init; } = default;
    public string DisplayName => FileMetadata.Name;
    public Uri DownloadPageUri => HttpDownloadJob.JobDefinition.DownloadPageUri;
    public EntityId MetadataEntityId => FileMetadata.Id;
    public IJob? InnerJob => HttpDownloadJob.Job;
    public Optional<LibraryFile.ReadOnly> FindLibraryFile(IDb db) => Optional<LibraryFile.ReadOnly>.None;
    
    // <see cref="IJobDefinitionWithStart{TJobDefinition,TResult}"/> implementation
    public async ValueTask<AbsolutePath> StartAsync(IJobContext<TestNexusModsDownloadJob> context)
    {
        // Signal that we're ready to start (for test synchronization)
        ReadySignal?.Set();
            
        // Wait for start signal if provided (allows tests to control timing)
        if (StartSignal != null)
        {
            if (!StartSignal.Wait(TimeSpan.FromSeconds(30), context.CancellationToken))
                throw new TimeoutException("StartSignal was not set within timeout period");
        }
        
        // Yield to allow other operations
        await context.YieldAsync();
            
        // Simply await the completion source - matches original NexusModsDownloadJob pattern
        return await CompletionSource.Task;
    }
    
    // <see cref="IDownloadJob"/> metadata implementation
    public ValueTask AddMetadata(ITransaction transaction, LibraryFile.New libraryFile)
    {
        // For testing purposes, we don't need to add any metadata
        return ValueTask.CompletedTask;
    }
}
