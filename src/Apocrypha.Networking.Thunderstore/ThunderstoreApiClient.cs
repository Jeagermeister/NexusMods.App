using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Abstractions.Thunderstore.DTOs;

namespace Apocrypha.Networking.Thunderstore;

/// <summary>
/// Client for the Thunderstore experimental API. All endpoints are anonymous and read-only.
/// </summary>
public class ThunderstoreApiClient : IThunderstoreApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ThunderstoreApiClient> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ThunderstoreApiClient(ILogger<ThunderstoreApiClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public Task<PackageDto?> GetPackage(PackageRef package, CancellationToken cancellationToken = default)
        => Get<PackageDto>(ThunderstoreUrls.GetPackageApiUri(package), cancellationToken);

    /// <inheritdoc/>
    public Task<PackageVersionDto?> GetVersion(PackageVersionRef version, CancellationToken cancellationToken = default)
        => Get<PackageVersionDto>(ThunderstoreUrls.GetVersionApiUri(version), cancellationToken);

    private async Task<T?> Get<T>(Uri uri, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (result is null) _logger.LogWarning("Thunderstore API returned an empty body for `{Uri}`", uri);
        return result;
    }
}
