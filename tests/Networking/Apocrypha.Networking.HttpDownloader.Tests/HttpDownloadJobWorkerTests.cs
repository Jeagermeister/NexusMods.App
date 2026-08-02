using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Hashing.xxHash3;
using NexusMods.Hashing.xxHash3.Paths;
using NexusMods.Paths;

namespace Apocrypha.Networking.HttpDownloader.Tests;

/// <summary>
/// Coverage for <see cref="HttpDownloadJob"/>, the worker every download in the app goes through.
///
/// <para>
/// This used to be a single test against <c>https://paris.nexus-cdn.com/100M</c>, marked
/// <c>RequiresNetworking</c> — which meant it ran in no CI lane at all, and the download worker had
/// zero coverage. It now runs against <see cref="LocalHttpServer"/>, so it is hermetic and runs
/// everywhere.
/// </para>
/// </summary>
public class HttpDownloadJobWorkerTests
{
    private readonly TemporaryFileManager _temporaryFileManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalHttpServer _server;

    public HttpDownloadJobWorkerTests(IServiceProvider serviceProvider)
    {
        _temporaryFileManager = serviceProvider.GetRequiredService<TemporaryFileManager>();
        _serviceProvider = serviceProvider;
        _server = serviceProvider.GetRequiredService<LocalHttpServer>();
    }

    /// <summary>
    /// The baseline contract: the bytes that land on disk are exactly the bytes the server served.
    /// </summary>
    [Fact]
    public async Task DownloadsTheCompleteFile()
    {
        var uri = new Uri(_server.Uri, LocalHttpServer.Payload);

        await using var outputPath = _temporaryFileManager.CreateFile();
        _ = await HttpDownloadJob.Create(_serviceProvider, uri, uri, outputPath.Path);

        outputPath.Path.FileExists.Should().BeTrue();
        await AssertContentMatchesServer(outputPath.Path);
    }

    /// <summary>
    /// A server that never advertises <c>Accept-Ranges</c> — the download must still complete
    /// correctly rather than depending on the resumable path.
    /// </summary>
    [Fact]
    public async Task DownloadsFromAServerThatDoesNotSupportRanges()
    {
        var uri = new Uri(_server.Uri, LocalHttpServer.PayloadWithoutRanges);

        await using var outputPath = _temporaryFileManager.CreateFile();
        _ = await HttpDownloadJob.Create(_serviceProvider, uri, uri, outputPath.Path);

        await AssertContentMatchesServer(outputPath.Path);
    }

    /// <summary>
    /// Deterministic coverage for the reset branch (ledger item 19c, non-range shape): the first
    /// connection dies after 3 MB of real progress, the server does not support ranges, so the
    /// retry's plain GET answers 200 while <c>TotalBytesDownloaded</c> is already past zero. The
    /// job must restart from offset 0 — keeping the stale prefix is silent corruption with a
    /// correct file size, which is exactly what the one observed failure of the test above looked
    /// like.
    /// </summary>
    [Fact]
    public async Task ResumesAfterATruncatedConnectionOnAServerWithoutRanges()
    {
        var id = $"{Guid.NewGuid():N}";
        var uri = new Uri(_server.Uri, $"{LocalHttpServer.PayloadTruncatedOnce}?id={id}");

        await using var outputPath = _temporaryFileManager.CreateFile();
        _ = await HttpDownloadJob.Create(_serviceProvider, uri, uri, outputPath.Path);

        await AssertContentMatchesServer(outputPath.Path, id);
    }

    /// <summary>
    /// Deterministic coverage for the reset branch's range shape — the exact path ledger item 19c
    /// could not reach: after real partial progress the retry sends a valid Range request, and the
    /// server answers 200 with the entire body instead of 206. The job must detect the full-body
    /// response and reset to offset 0 rather than appending the whole file after the stale prefix.
    /// </summary>
    [Fact]
    public async Task ResetsWhenAServerAnswersARangeRequestWithTheFullBody()
    {
        var id = $"{Guid.NewGuid():N}";
        var uri = new Uri(_server.Uri, $"{LocalHttpServer.PayloadRangeIgnored}?id={id}");

        await using var outputPath = _temporaryFileManager.CreateFile();
        _ = await HttpDownloadJob.Create(_serviceProvider, uri, uri, outputPath.Path);

        await AssertContentMatchesServer(outputPath.Path, id);
    }

    /// <summary>
    /// Compares the downloaded file against what the server holds, and on a mismatch says *where* it
    /// diverged.
    /// </summary>
    /// <remarks>
    /// Note what is deliberately NOT asserted: file size on its own. The download pre-allocates the
    /// destination to the advertised content length, so a download that stopped half way still has
    /// exactly the right size — checking it passes while the content is wrong, which is how the one
    /// observed failure of this test presented (a bare hash mismatch with a correct size, and no
    /// clue whether the body was short, misaligned, or written at the wrong offset). The first
    /// differing offset and the zero-tail length distinguish those cases.
    /// </remarks>
    private async Task AssertContentMatchesServer(AbsolutePath path, string? journalId = null)
    {
        var expected = _server.LargeData;
        var actual = await path.ReadAllBytesAsync();

        var hash = await path.XxHash3Async();
        if (hash == _server.LargeDataHash) return;

        var firstDifference = -1;
        var shared = Math.Min(actual.Length, expected.Length);
        for (var i = 0; i < shared; i++)
        {
            if (actual[i] == expected[i]) continue;
            firstDifference = i;
            break;
        }

        var zeroTail = 0;
        for (var i = actual.Length - 1; i >= 0 && actual[i] == 0; i--) zeroTail++;

        // The server-side request journal turns "wrong bytes" into a full request trace: which
        // GETs arrived, with what Range headers, and what the server actually wrote for each.
        var journal = journalId is null ? "<none>" : string.Join(" | ", _server.GetRequestJournal(journalId));

        hash.Should().Be(_server.LargeDataHash,
            "the downloaded bytes should match the served bytes (length {0} vs expected {1}, first difference at {2}, {3} zero bytes at the end; server journal: {4})",
            actual.Length, expected.Length, firstDifference, zeroTail, journal);
    }
}
