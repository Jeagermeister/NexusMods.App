using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Apocrypha.Networking.HttpDownloader;

/// <summary>
/// Counts outgoing HTTP requests per endpoint and surfaces the Nexus rate-limit budget.
/// </summary>
/// <remarks>
/// <para>
/// Motivated by two evenings of the Nexus API quota draining to zero during large collection
/// operations with nothing in the logs to say where the requests went. The app previously had no
/// HTTP-level observability at all, so diagnosing "what is hammering the API" meant guessing from
/// CPU graphs. This class is the counter; <see cref="HttpTrafficMonitorHandler"/> feeds it.
/// </para>
/// <para>
/// Reporting is deliberately quiet: nothing is logged until <see cref="DumpThreshold"/> requests
/// have accumulated since the last report, so idle sessions stay silent while a storm produces a
/// steady stream of summaries naming the offending endpoints. Rate-limit exhaustion (HTTP 429 or a
/// near-zero remaining budget) is logged immediately.
/// </para>
/// </remarks>
public sealed class HttpTrafficMonitor
{
    /// <summary>Requests between summary log lines. A storm at ~4 req/s reports every ~50 seconds.</summary>
    private const int DumpThreshold = 200;

    /// <summary>Remaining-budget level that triggers an immediate warning.</summary>
    private const int LowBudgetThreshold = 25;

    private readonly ILogger<HttpTrafficMonitor> _logger;
    private readonly ConcurrentDictionary<string, long> _endpointCounts = new();

    private long _sinceLastDump;
    private long _total;

    // Last-seen Nexus rate-limit headers (REST only; GraphQL does not expose them).
    private long _hourlyRemaining = -1;
    private long _dailyRemaining = -1;
    private int _lowBudgetWarned;

    public HttpTrafficMonitor(ILogger<HttpTrafficMonitor> logger)
    {
        _logger = logger;
    }

    /// <summary>Total requests recorded over the process lifetime.</summary>
    public long TotalRequests => Interlocked.Read(ref _total);

    /// <summary>Snapshot of per-endpoint counts, for verbs and tests.</summary>
    public IReadOnlyDictionary<string, long> SnapshotCounts() => _endpointCounts.ToDictionary();

    public void Record(HttpRequestMessage request, HttpResponseMessage? response)
    {
        var endpoint = ClassifyEndpoint(request);
        _endpointCounts.AddOrUpdate(endpoint, 1, static (_, count) => count + 1);
        Interlocked.Increment(ref _total);

        if (response is not null)
            RecordResponse(endpoint, response);

        if (Interlocked.Increment(ref _sinceLastDump) >= DumpThreshold)
        {
            Interlocked.Exchange(ref _sinceLastDump, 0);
            DumpSummary();
        }
    }

    private void RecordResponse(string endpoint, HttpResponseMessage response)
    {
        if (TryReadHeader(response, "x-rl-hourly-remaining", out var hourly))
            Interlocked.Exchange(ref _hourlyRemaining, hourly);
        if (TryReadHeader(response, "x-rl-daily-remaining", out var daily))
            Interlocked.Exchange(ref _dailyRemaining, daily);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Nexus rate limit hit (HTTP 429) on {Endpoint}; hourly remaining: {Hourly}, daily remaining: {Daily}",
                endpoint, _hourlyRemaining, _dailyRemaining);
            return;
        }

        // Warn once per drop below the threshold rather than on every request while low.
        var hourlyNow = Interlocked.Read(ref _hourlyRemaining);
        var dailyNow = Interlocked.Read(ref _dailyRemaining);
        var isLow = (hourlyNow >= 0 && hourlyNow < LowBudgetThreshold) || (dailyNow >= 0 && dailyNow < LowBudgetThreshold);
        if (isLow)
        {
            if (Interlocked.Exchange(ref _lowBudgetWarned, 1) == 0)
            {
                _logger.LogWarning("Nexus API budget is nearly exhausted: hourly remaining {Hourly}, daily remaining {Daily}. Recent traffic follows",
                    hourlyNow, dailyNow);
                DumpSummary();
            }
        }
        else
        {
            Interlocked.Exchange(ref _lowBudgetWarned, 0);
        }
    }

    private void DumpSummary()
    {
        var top = _endpointCounts
            .OrderByDescending(static kv => kv.Value)
            .Take(8)
            .Select(static kv => $"{kv.Key}={kv.Value}");

        _logger.LogInformation(
            "HTTP traffic: {Total} request(s) total; top endpoints: {Endpoints}; Nexus budget: hourly {Hourly}, daily {Daily} (-1 = not yet seen)",
            TotalRequests, string.Join(", ", top), Interlocked.Read(ref _hourlyRemaining), Interlocked.Read(ref _dailyRemaining));
    }

    private static bool TryReadHeader(HttpResponseMessage response, string name, out long value)
    {
        value = -1;
        return response.Headers.TryGetValues(name, out var values)
               && long.TryParse(values.FirstOrDefault(), out value);
    }

    /// <summary>
    /// Reduces a request to a low-cardinality "host/path-template" key: numeric ids, GUIDs, and
    /// hex hashes become placeholders so ten thousand download-link calls count as one endpoint.
    /// </summary>
    internal static string ClassifyEndpoint(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null) return "(no-uri)";

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(ClassifySegment);

        return $"{uri.Host}/{string.Join('/', segments)}";
    }

    private static string ClassifySegment(string segment)
    {
        // Keep trailing static suffixes like "12345.json" recognisable.
        var withoutSuffix = segment.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? segment[..^5]
            : segment;

        if (withoutSuffix.Length > 0 && withoutSuffix.All(char.IsAsciiDigit))
            return segment.Length == withoutSuffix.Length ? "{id}" : "{id}.json";

        // Hash before GUID: a 32-char undashed hex string (an MD5) also parses as a "N"-format GUID.
        if (withoutSuffix.Length >= 32 && withoutSuffix.All(char.IsAsciiHexDigit))
            return "{hash}";

        if (Guid.TryParse(withoutSuffix, out _))
            return "{guid}";

        return segment;
    }
}
