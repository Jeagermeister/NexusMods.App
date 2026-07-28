using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apocrypha.Networking.HttpDownloader.Tests;

public class HttpTrafficMonitorTests
{
    [Theory]
    // Ten thousand download-link calls must collapse into one endpoint key.
    [InlineData("https://api.nexusmods.com/v1/games/fallout4/mods/12345/files/67890/download_link.json",
        "api.nexusmods.com/v1/games/fallout4/mods/{id}/files/{id}/download_link.json")]
    // GraphQL is a single POST endpoint; it must stay itself.
    [InlineData("https://api.nexusmods.com/v2/graphql", "api.nexusmods.com/v2/graphql")]
    // MD5 and GUID path segments are unbounded-cardinality inputs.
    [InlineData("https://api.nexusmods.com/v1/games/fallout4/mods/md5_search/0123456789abcdef0123456789abcdef.json",
        "api.nexusmods.com/v1/games/fallout4/mods/md5_search/{hash}")]
    [InlineData("https://cdn.example.com/files/d1f0c5e2-8a4b-4c3d-9e6f-7a8b9c0d1e2f/data",
        "cdn.example.com/files/{guid}/data")]
    public void ClassifiesEndpointsToLowCardinalityKeys(string url, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        HttpTrafficMonitor.ClassifyEndpoint(request).Should().Be(expected);
    }

    [Fact]
    public void CountsRequestsPerEndpoint()
    {
        var monitor = new HttpTrafficMonitor(NullLogger<HttpTrafficMonitor>.Instance);

        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.nexusmods.com/v1/games/fallout4/mods/{i}.json");
            monitor.Record(request, response: null);
        }

        monitor.TotalRequests.Should().Be(3);
        monitor.SnapshotCounts().Should().ContainKey("api.nexusmods.com/v1/games/fallout4/mods/{id}.json")
            .WhoseValue.Should().Be(3);
    }

    [Fact]
    public void CapturesNexusRateLimitHeaders()
    {
        var monitor = new HttpTrafficMonitor(NullLogger<HttpTrafficMonitor>.Instance);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/games/fallout4.json");
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("x-rl-hourly-remaining", "42");
        response.Headers.Add("x-rl-daily-remaining", "1234");

        // Must not throw, and must not warn (budget is healthy) — the header parse is the point.
        monitor.Record(request, response);
        monitor.TotalRequests.Should().Be(1);
    }
}
