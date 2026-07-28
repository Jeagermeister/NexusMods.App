namespace Apocrypha.Networking.HttpDownloader;

/// <summary>
/// Delegating handler that reports every request/response pair to <see cref="HttpTrafficMonitor"/>.
/// Pass-through otherwise: it never alters, blocks, or retries traffic.
/// </summary>
public sealed class HttpTrafficMonitorHandler : DelegatingHandler
{
    private readonly HttpTrafficMonitor _monitor;

    public HttpTrafficMonitorHandler(HttpTrafficMonitor monitor)
    {
        _monitor = monitor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            return response;
        }
        finally
        {
            // Counting must never break a request; a monitor exception would surface as a
            // download/API failure otherwise.
            try { _monitor.Record(request, response); }
            catch { /* deliberately swallowed */ }
        }
    }
}
