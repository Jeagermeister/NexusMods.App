using DynamicData;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Downloads;
using Apocrypha.Library.Tests.DownloadsService.Helpers;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.NexusModsApi;
using Xunit;
using SyncHelpers = Apocrypha.Library.Tests.DownloadsService.Helpers.SynchronizationHelpers;

namespace Apocrypha.Library.Tests.DownloadsService;

public class DownloadsServiceTests(
    IJobMonitor jobMonitor,
    Library.DownloadsService service,
    IServiceProvider serviceProvider)
{
    private readonly DownloadJobFactory _jobFactory = new(jobMonitor, serviceProvider);

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

        // Track by download id: an Update is a replacement, not another item, so the counts
        // below are immune to a status/progress Update racing the Add (ledger item 19b).
        using var allDownloads = new DownloadCollectionTracker(service.AllDownloads);
        using var completedDownloads = new DownloadCollectionTracker(service.CompletedDownloads);
        using var activeDownloads = new DownloadCollectionTracker(service.ActiveDownloads);
        using var gameDownloads = new DownloadCollectionTracker(service.GetDownloadsForGame(GameId.From(gameId.Value)));

        // 1. No jobs initially
        allDownloads.Count.Should().Be(0, "no jobs should exist initially");
        completedDownloads.Count.Should().Be(0, "no completed jobs should exist initially");
        activeDownloads.Count.Should().Be(0, "no active jobs should exist initially");
        gameDownloads.Count.Should().Be(0, "no game-specific jobs should exist initially");

        // 2. Start job with signals for proper synchronization
        var context = _jobFactory.CreateAndStartDownloadJob(gameId);

        // Wait for jobs to signal they're ready before checking state
        context.WaitForJobsReady(TimeSpan.FromSeconds(30))
            .Should().BeTrue("jobs should signal ready within timeout");

        // Signal jobs to start and wait for collections to be updated
        context.SignalJobsToStart();

        // Wait for the job to appear; the timeout is a failure backstop, not the mechanism
        (await allDownloads.WaitForCount(1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in AllDownloads when started");
        (await gameDownloads.WaitForCount(1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in game-specific downloads when started");
        (await activeDownloads.WaitForCount(1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in ActiveDownloads when started");
        completedDownloads.Count.Should().Be(0, "job should not be in CompletedDownloads when started");

        // 3. Complete job - should move to CompletedDownloads only
        context.CompleteJob();
        await context.JobTask.Job.WaitAsync();

        // Wait for completion to be processed by collections
        (await completedDownloads.WaitForCount(1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("completed job should be in CompletedDownloads");
        (await activeDownloads.WaitForCount(0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("completed job should not be in ActiveDownloads");

        // Verify final state
        allDownloads.Count.Should().Be(1, "completed job should remain in AllDownloads");
        completedDownloads.Count.Should().Be(1, "completed job should be in CompletedDownloads");
        activeDownloads.Count.Should().Be(0, "completed job should not be in ActiveDownloads");
        gameDownloads.Count.Should().Be(1, "completed job should remain in game-specific downloads");
    }
    
    [Fact]
    public async Task CancelledJobs_ShouldBeCompletelyRemoved_FromAllCollections()
    {
        // Arrange
        var gameId = NexusModsGameId.From(1234u);

        using var allDownloads = new DownloadCollectionTracker(service.AllDownloads);
        using var completedDownloads = new DownloadCollectionTracker(service.CompletedDownloads);
        using var activeDownloads = new DownloadCollectionTracker(service.ActiveDownloads);

        // Initially empty
        allDownloads.Count.Should().Be(0, "no jobs should exist initially");

        // Create and start job
        var context = _jobFactory.CreateAndStartDownloadJob(gameId);

        // Wait for jobs to signal they're ready before checking state
        context.WaitForJobsReady(TimeSpan.FromSeconds(30))
            .Should().BeTrue("jobs should signal ready within timeout");

        // Job should appear in collections
        (await allDownloads.WaitForCount(1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in AllDownloads when started");
        (await activeDownloads.WaitForCount(1, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("job should be in ActiveDownloads when started");

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
        (await allDownloads.WaitForCount(0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("cancelled jobs should be removed from AllDownloads");
        (await completedDownloads.WaitForCount(0, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("cancelled jobs should not be in CompletedDownloads");
        (await activeDownloads.WaitForCount(0, TimeSpan.FromSeconds(30)))
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
