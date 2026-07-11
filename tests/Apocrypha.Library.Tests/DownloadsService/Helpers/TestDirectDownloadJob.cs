using DynamicData.Kernel;
using Apocrypha.Abstractions.Downloads;
using Apocrypha.Abstractions.HttpDownloads;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Library.Tests.DownloadsService.Helpers;

/// <summary>
/// Test double shaped like ThunderstoreDownloadJob: the job IS the HTTP transfer
/// (<see cref="ILibraryDownloadJob.InnerJob"/> is null) rather than wrapping an inner
/// HTTP job like the Nexus download job does. Used to verify the downloads service
/// handles both shapes.
/// </summary>
public record TestDirectDownloadJob : IJobDefinitionWithStart<TestDirectDownloadJob, AbsolutePath>, IHttpDownloadJob, ILibraryDownloadJob
{
    public required Uri Uri { get; init; }
    public required Uri DownloadPageUri { get; init; }
    public required AbsolutePath Destination { get; init; }
    public required TaskCompletionSource<AbsolutePath> CompletionSource { get; init; }
    public required string DisplayName { get; init; }

    public GameId GameId { get; init; } = default;
    public EntityId MetadataEntityId { get; init; } = default;
    public IJob? InnerJob => null;

    // Synchronization signals for deterministic testing
    public ManualResetEventSlim? StartSignal { get; init; }
    public ManualResetEventSlim? ReadySignal { get; init; }

    private readonly TestHttpDownloadState _state = new();

    public bool SupportsPausing => true;

    public IPublicJobStateData? GetJobStateData() => _state;

    public async ValueTask<AbsolutePath> StartAsync(IJobContext<TestDirectDownloadJob> context)
    {
        ReadySignal?.Set();

        if (StartSignal != null)
        {
            if (!StartSignal.Wait(TimeSpan.FromSeconds(30), context.CancellationToken))
                throw new TimeoutException("StartSignal was not set within timeout period");
        }

        // Pause/resume in the jobs system is cooperative: it takes effect at YieldAsync
        // checkpoints, so poll the completion source with periodic yields.
        while (!CompletionSource.Task.IsCompleted)
        {
            await context.YieldAsync();
            await Task.WhenAny(CompletionSource.Task, Task.Delay(10, context.CancellationToken));
        }

        return await CompletionSource.Task;
    }

    public ValueTask AddMetadata(ITransaction transaction, LibraryFile.New libraryFile)
    {
        return ValueTask.CompletedTask;
    }

    public Optional<LibraryFile.ReadOnly> FindLibraryFile(IDb db) => Optional<LibraryFile.ReadOnly>.None;
}
