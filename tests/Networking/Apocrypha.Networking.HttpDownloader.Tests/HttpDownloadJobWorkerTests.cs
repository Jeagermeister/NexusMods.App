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
    private async Task AssertContentMatchesServer(AbsolutePath path)
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

        hash.Should().Be(_server.LargeDataHash,
            "the downloaded bytes should match the served bytes (length {0} vs expected {1}, first difference at {2}, {3} zero bytes at the end)",
            actual.Length, expected.Length, firstDifference, zeroTail);
    }
}
