using System.Net;
using System.Net.Http;
using NexusMods.Paths;
using NexusMods.Sdk.IO;

namespace NexusMods.Sdk.Tests.IO;

public class CachedHttpStreamFactoryTests
{
    private static readonly byte[] RemoteBytes = [0xCA, 0xFE, 0xBA, 0xBE];
    private static readonly byte[] FallbackBytes = [0xDE, 0xAD];
    private static readonly Uri Uri = new("https://example.test/assets/cover.webp");

    [Test]
    public async Task FirstReadDownloadsThenCaches()
    {
        var (factory, handler, cacheFile) = Setup();

        var bytes = await ReadAll(factory);

        await Assert.That(bytes).IsEquivalentTo(RemoteBytes);
        await Assert.That(cacheFile.FileExists).IsTrue();
        await Assert.That(handler.RequestCount).IsEqualTo(1);
    }

    [Test]
    public async Task LaterReadsServeTheCacheWithoutHttp()
    {
        var (factory, handler, _) = Setup();

        await ReadAll(factory);
        var bytes = await ReadAll(factory);

        await Assert.That(bytes).IsEquivalentTo(RemoteBytes);
        await Assert.That(handler.RequestCount).IsEqualTo(1);
    }

    [Test]
    public async Task PreexistingCacheFileIsServedWithoutHttp()
    {
        var (factory, handler, cacheFile) = Setup();
        cacheFile.Parent.CreateDirectory();
        await using (var stream = cacheFile.Create())
        {
            await stream.WriteAsync(RemoteBytes);
        }

        var bytes = await ReadAll(factory);

        await Assert.That(bytes).IsEquivalentTo(RemoteBytes);
        await Assert.That(handler.RequestCount).IsEqualTo(0);
    }

    [Test]
    public async Task FailedDownloadServesTheFallbackAndRetriesNextRead()
    {
        var (factory, handler, cacheFile) = Setup();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var fallbackRead = await ReadAll(factory);
        await Assert.That(fallbackRead).IsEquivalentTo(FallbackBytes);
        await Assert.That(cacheFile.FileExists).IsFalse();

        // The CDN comes back: the next read downloads instead of serving a poisoned cache.
        handler.Responder = OkResponder;
        var recoveredRead = await ReadAll(factory);
        await Assert.That(recoveredRead).IsEquivalentTo(RemoteBytes);
        await Assert.That(cacheFile.FileExists).IsTrue();
    }

    [Test]
    public async Task ConcurrentReadsDownloadOnce()
    {
        var (factory, handler, _) = Setup();
        handler.Delay = TimeSpan.FromMilliseconds(50);

        var reads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ReadAll(factory))));

        foreach (var bytes in reads) await Assert.That(bytes).IsEquivalentTo(RemoteBytes);
        await Assert.That(handler.RequestCount).IsEqualTo(1);
    }

    [Test]
    public async Task AbortedDownloadLeavesNoCacheEntryBehind()
    {
        var (factory, handler, cacheFile) = Setup();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(),
        };

        var bytes = await ReadAll(factory);

        await Assert.That(bytes).IsEquivalentTo(FallbackBytes);
        await Assert.That(cacheFile.FileExists).IsFalse();
        var leftovers = cacheFile.Parent.EnumerateFiles().ToArray();
        await Assert.That(leftovers).IsEmpty();
    }

    private static (CachedHttpStreamFactory Factory, CountingHandler Handler, AbsolutePath CacheFile) Setup()
    {
        var fileSystem = new InMemoryFileSystem();
        var cacheFile = fileSystem.GetKnownPath(KnownPath.CurrentDirectory).Combine("cache/GameArt/cover.webp");
        var handler = new CountingHandler { Responder = OkResponder };
        var factory = new CachedHttpStreamFactory(new HttpClient(handler), Uri, cacheFile, new FreshStreamFallback());
        return (factory, handler, cacheFile);
    }

    private static HttpResponseMessage OkResponder(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(RemoteBytes),
    };

    private static async Task<byte[]> ReadAll(IStreamFactory factory)
    {
        await using var stream = await factory.GetStreamAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;
        public required Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return Responder(request);
        }
    }

    /// <summary>Unlike <see cref="MemoryStreamFactory"/>, serves a fresh stream per read — the factory may be read many times.</summary>
    private sealed class FreshStreamFallback : IStreamFactory
    {
        public RelativePath FileName => "fallback.webp";
        public ValueTask<Stream> GetStreamAsync() => ValueTask.FromResult<Stream>(new MemoryStream(FallbackBytes));
    }

    /// <summary>Dies mid-body: headers say OK, the content stream then fails the copy.</summary>
    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new IOException("simulated mid-download failure");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
