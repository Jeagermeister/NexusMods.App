using Microsoft.Extensions.Logging;
using NexusMods.Paths;

namespace NexusMods.Sdk.IO;

/// <summary>
/// A Stream Factory backed by a remote HTTP resource with a local disk cache: the first read
/// downloads the resource to <see cref="CacheFile"/> (via a temp file + atomic rename, so a
/// crash mid-download never leaves a half-written cache entry), and every later read serves
/// the cached file. When the resource is uncached and unreachable (offline, 404, timeout) the
/// read serves the fallback factory instead of throwing; the download is retried on the next
/// read.
/// </summary>
public class CachedHttpStreamFactory : IStreamFactory
{
    /// <summary>
    /// Bounds how long a single read waits on the network before serving the fallback; the
    /// shared <see cref="HttpClient"/>'s own timeout (100s default) is UI-hostile for
    /// best-effort resources like artwork.
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly IStreamFactory _fallback;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _downloadLock = new(initialCount: 1, maxCount: 1);

    /// <summary/>
    /// <param name="httpClient">Client used for the one-time download.</param>
    /// <param name="uri">The remote resource.</param>
    /// <param name="cacheFile">Where the downloaded resource is cached on disk.</param>
    /// <param name="fallback">Served whenever the resource is uncached and undownloadable.</param>
    /// <param name="logger">Optional; failed downloads are logged as debug (they self-heal on a later read).</param>
    public CachedHttpStreamFactory(HttpClient httpClient, Uri uri, AbsolutePath cacheFile, IStreamFactory fallback, ILogger? logger = null)
    {
        _httpClient = httpClient;
        Uri = uri;
        CacheFile = cacheFile;
        _fallback = fallback;
        _logger = logger;
    }

    /// <summary>The remote resource.</summary>
    public Uri Uri { get; }

    /// <summary>Where the downloaded resource is cached on disk.</summary>
    public AbsolutePath CacheFile { get; }

    /// <inheritdoc/>
    public RelativePath FileName => CacheFile.Name;

    /// <inheritdoc/>
    public async ValueTask<Stream> GetStreamAsync()
    {
        try
        {
            if (!CacheFile.FileExists) await DownloadOnceAsync();
            return CacheFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception)
        {
            _logger?.LogDebug(exception, "Serving fallback for `{Uri}`: download or cache read failed", Uri);
            return await _fallback.GetStreamAsync();
        }
    }

    private async Task DownloadOnceAsync()
    {
        await _downloadLock.WaitAsync();
        try
        {
            // A concurrent read may have finished the download while this one waited.
            if (CacheFile.FileExists) return;

            using var timeout = new CancellationTokenSource(DownloadTimeout);
            using var response = await _httpClient.GetAsync(Uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();

            CacheFile.Parent.CreateDirectory();
            var tempFile = CacheFile.Parent.Combine($"{CacheFile.Name}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var tempStream = tempFile.Create())
                {
                    await response.Content.CopyToAsync(tempStream, timeout.Token);
                }

                // Atomic publish; `overwrite` also settles races with another process caching
                // the same resource (same content, either copy wins).
                await tempFile.MoveToAsync(CacheFile, overwrite: true, token: CancellationToken.None);
            }
            catch
            {
                if (tempFile.FileExists) tempFile.Delete();
                throw;
            }
        }
        finally
        {
            _downloadLock.Release();
        }
    }
}
