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
        outputPath.Path.FileInfo.Size.Should().Be(Size.FromLong(_server.LargeData.Length));

        var hash = await outputPath.Path.XxHash3Async();
        hash.Should().Be(_server.LargeDataHash);
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

        outputPath.Path.FileInfo.Size.Should().Be(Size.FromLong(_server.LargeData.Length));

        var hash = await outputPath.Path.XxHash3Async();
        hash.Should().Be(_server.LargeDataHash);
    }
}
