using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;

namespace Apocrypha.Networking.HttpDownloader.Tests;

public class LocalHttpServer : IDisposable
{
    private const int MB = 1024 * 1024;

    /// <summary>Serves <see cref="LargeData"/>, supporting ranged requests.</summary>
    public const string Payload = "/payload";

    /// <summary>Serves <see cref="LargeData"/> as a server that cannot resume.</summary>
    public const string PayloadWithoutRanges = "/payload-no-ranges";

    private readonly ILogger<LocalHttpServer> _logger;
    private readonly HttpListener _listener;
    private readonly string _prefix;

    // Built on first use, not in the constructor: this class is a DI singleton for the whole test
    // assembly, and eagerly allocating the payload made merely resolving it cost hundreds of MB.
    private readonly Lazy<byte[]> _largeData = new(GenerateLargeData);

    private readonly Lazy<Hash> _largeDataHash;

    public byte[] LargeData => _largeData.Value;
    public Hash LargeDataHash => _largeDataHash.Value;

    public LocalHttpServer(ILogger<LocalHttpServer> logger)
    {
        _logger = logger;
        _largeDataHash = new Lazy<Hash>(() => LargeData.AsSpan().xxHash3());
        (_listener, _prefix) = CreateNewListener();

        StartLoop();
    }

    /// <summary>
    /// Deterministic bytes, big enough to span many read buffers and to make a ranged request
    /// meaningful, small enough that CI does not pay for it. It was 512 MB, which is why nothing
    /// could afford to use this server.
    /// </summary>
    private static byte[] GenerateLargeData()
    {
        var data = new byte[8 * MB];
        for (var offset = 0; offset < data.Length; offset++)
            data[offset] = (byte)(offset % 251);

        return data;
    }

    private void StartLoop()
    {
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    // Disposed while awaiting a connection: that is how this loop is meant to end.
                    return;
                }

                // One malformed request must not take the server down. This is a DI singleton shared
                // by every test in the assembly, so an escaping exception here used to end the loop
                // and leave every later test hanging on a dead listener with no clue why.
                try
                {
                    await Handle(context);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to serve {Path}", context.Request.Url?.PathAndQuery);
                    try
                    {
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }
                    catch
                    {
                        // The response may already be committed or disposed; nothing useful to do.
                    }
                }
            }
        });
    }

    private async Task Handle(HttpListenerContext context)
    {
        _logger.LogInformation("Got connection");
        using var resp = context.Response;

        if (context.Request.Url?.PathAndQuery.StartsWith("/Resources") ?? false)
        {
            await ServeResource(resp, context.Request);
            return;
        }

        switch (context.Request.Url?.PathAndQuery)
        {
            case "/hello":
            {
                var responseString = Encoding.UTF8.GetBytes("Hello World!");
                resp.StatusCode = 200;
                resp.StatusDescription = "OK";
                resp.ProtocolVersion = HttpVersion.Version11;
                resp.ContentLength64 = responseString.Length;
                if (context.Request.HttpMethod != "HEAD")
                {
                    await using var ros = resp.OutputStream;
                    await ros.WriteAsync(responseString);
                }

                break;
            }
            case Payload:
                await ServeLargeData(resp, context.Request, acceptRanges: true);
                break;
            case PayloadWithoutRanges:
                await ServeLargeData(resp, context.Request, acceptRanges: false);
                break;
            case "/reliable":
                await HandleUnreliable(resp, context.Request, false);
                break;
            case "/unreliable":
                await HandleUnreliable(resp, context.Request, true);
                break;
            default:
            {
                resp.StatusCode = 404;
                resp.StatusDescription = "Not Found";
                break;
            }
        }
    }

    /// <summary>
    /// A well-behaved download endpoint serving <see cref="LargeData"/>. With
    /// <paramref name="acceptRanges"/> false it behaves like a server that cannot resume: it never
    /// advertises <c>Accept-Ranges</c> and answers even a ranged request with the whole body.
    /// </summary>
    private async Task ServeLargeData(HttpListenerResponse resp, HttpListenerRequest request, bool acceptRanges)
    {
        var data = LargeData;
        var range = acceptRanges ? request.Headers.Get("Range") : null;

        resp.ProtocolVersion = HttpVersion.Version11;
        resp.Headers.Add(HttpResponseHeader.ContentType, "application/octet-stream");
        if (acceptRanges) resp.Headers.Add(HttpResponseHeader.AcceptRanges, "bytes");

        if (range is null)
        {
            resp.StatusCode = (int)HttpStatusCode.OK;
            resp.StatusDescription = "OK";
            resp.ContentLength64 = data.Length;

            if (request.HttpMethod == "HEAD")
            {
                await using var _ = resp.OutputStream;
                return;
            }

            await using var ros = resp.OutputStream;
            await ros.WriteAsync(data);
            return;
        }

        var rangeValue = RangeHeaderValue.Parse(range).Ranges.First();
        resp.StatusCode = (int)HttpStatusCode.PartialContent;
        resp.StatusDescription = "Partial Content";
        resp.Headers.Add(HttpResponseHeader.ContentRange, rangeValue.ToString());

        if (request.HttpMethod == "HEAD")
        {
            await using var _ = resp.OutputStream;
            return;
        }

        await SendContent(resp, new MemoryStream(data), rangeValue);
    }

    private async Task ServeResource(HttpListenerResponse resp, HttpListenerRequest request)
    {
        var filePath = Uri.UnescapeDataString(request.Url!.AbsolutePath);
        var fullPath = Path.GetFullPath("."+filePath);

        await using var stream = FileSystem.Shared.FromUnsanitizedFullPath(fullPath).Read();

        if (request.HttpMethod == "HEAD")
        {
            resp.StatusCode = (int)HttpStatusCode.OK;
            resp.StatusDescription = "OK";
            resp.ProtocolVersion = HttpVersion.Version11;
            resp.ContentLength64 = stream.Length;
            resp.Headers.Add(HttpResponseHeader.ContentType, "application/octet-stream");
            resp.Headers.Add(HttpResponseHeader.AcceptRanges, "bytes");
            resp.Headers.Add(HttpResponseHeader.KeepAlive, "true");
            await using var _ = resp.OutputStream;
            return;
        }

        var rangeString = request.Headers.Get("Range");


        if (rangeString == null)
        {
            resp.StatusCode = (int)HttpStatusCode.OK;
            resp.StatusDescription = "OK";
            resp.ProtocolVersion = HttpVersion.Version11;
            resp.Headers.Add(HttpResponseHeader.ContentType, "application/octet-stream");
            resp.Headers.Add(HttpResponseHeader.AcceptRanges, "bytes");
            resp.Headers.Add(HttpResponseHeader.KeepAlive, "true");
            resp.ContentLength64 = stream.Length;
            await using var ros = resp.OutputStream;
            await stream.CopyToAsync(ros);

        }
        else
        {
            var rangeValue = RangeHeaderValue.Parse(rangeString!);
            var range = rangeValue.Ranges.First();
            resp.StatusCode = (int)HttpStatusCode.PartialContent;
            resp.StatusDescription = "Partial Content";
            resp.ProtocolVersion = HttpVersion.Version11;
            resp.Headers.Add(HttpResponseHeader.ContentType, "application/octet-stream");
            resp.Headers.Add(HttpResponseHeader.AcceptRanges, "bytes");
            resp.Headers.Add(HttpResponseHeader.KeepAlive, "true");
            resp.Headers.Add(HttpResponseHeader.ContentRange, range.ToString());

            await SendContent(resp, stream, range);
        }
    }

    private async Task HandleUnreliable(HttpListenerResponse resp, HttpListenerRequest request, bool truncate)
    {
        if (request.HttpMethod == "HEAD")
        {
            resp.StatusCode = (int)HttpStatusCode.OK;
            resp.StatusDescription = "OK";
            resp.ProtocolVersion = HttpVersion.Version11;
            resp.ContentLength64 = LargeData.Length;
            resp.Headers.Add(HttpResponseHeader.ContentType, "application/octet-stream");
            resp.Headers.Add(HttpResponseHeader.AcceptRanges, "bytes");
            resp.Headers.Add(HttpResponseHeader.KeepAlive, "true");
            await using var _ = resp.OutputStream;
            return;
        }

        var rangeString = request.Headers.Get("Range");
        if (rangeString is null)
        {
            // These endpoints only ever answer 206, so a caller that forgot the Range header used to
            // get a NullReferenceException here -- which, before the loop was guarded, killed the
            // server for every subsequent test in the assembly.
            resp.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
            resp.StatusDescription = "Range Not Satisfiable";
            return;
        }

        var rangeValue = RangeHeaderValue.Parse(rangeString);
        var range = rangeValue.Ranges.First();

        resp.StatusCode = (int)HttpStatusCode.PartialContent;
        resp.StatusDescription = "Partial Content";
        resp.ProtocolVersion = HttpVersion.Version11;
        resp.Headers.Add(HttpResponseHeader.ContentType, "application/octet-stream");
        resp.Headers.Add(HttpResponseHeader.AcceptRanges, "bytes");
        resp.Headers.Add(HttpResponseHeader.KeepAlive, "true");
        resp.Headers.Add(HttpResponseHeader.ContentRange, range.ToString());
        await SendContent(resp, new MemoryStream(LargeData), range, truncate);

    }

    private async Task SendContent(HttpListenerResponse resp, Stream src, RangeItemHeaderValue range, bool truncate = false)
    {
        var from = range.From ?? 0;
        var to = range.To ?? src.Length;
        await using var ros = resp.OutputStream;
        src.Position = from;

        var count = to - from + 1;

        if (truncate && count > MB * 2)
            count = Random.Shared.Next(MB, MB * 2);

        var buffer = new byte[count];
        await src.ReadExactlyAsync(buffer);
        await ros.WriteAsync(buffer);
    }

    public Uri Uri => new(_prefix);

    private (HttpListener Listener, string Prefix) CreateNewListener()
    {
        HttpListener mListener;
        while (true)
        {
            mListener = new HttpListener();
            var newPort = Random.Shared.Next(49152, 65535);
            mListener.Prefixes.Add($"http://127.0.0.1:{newPort}/");
            try
            {
                mListener.Start();
            }
            catch
            {
                continue;
            }
            break;
        }

        return (mListener, mListener.Prefixes.First());
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
