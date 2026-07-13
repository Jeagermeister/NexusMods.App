using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModIo.DTOs;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Networking.ModIo;

/// <summary>
/// Client for the mod.io REST API (v1). Read-only api_key access; the key comes from
/// <see cref="ModIoSettings.ApiKey"/> at call time so a key set mid-session works without
/// a restart.
/// </summary>
public class ModIoApiClient : IModIoApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ModIoApiClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly ISettingsManager _settingsManager;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ModIoApiClient(ILogger<ModIoApiClient> logger, HttpClient httpClient, ISettingsManager settingsManager)
    {
        _logger = logger;
        _httpClient = httpClient;
        _settingsManager = settingsManager;
    }

    private string GetApiKey()
    {
        var apiKey = _settingsManager.Get<ModIoSettings>().ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ModIoApiKeyMissingException();
        return apiKey;
    }

    /// <inheritdoc/>
    public async Task<GameDto?> GetGameByNameId(string gameNameId, CancellationToken cancellationToken = default)
    {
        var page = await Get<PagedResultDto<GameDto>>(ModIoUrls.GetGamesApiUri(GetApiKey(), nameId: gameNameId), cancellationToken);
        return page?.Data.FirstOrDefault(game => string.Equals(game.NameId, gameNameId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public async Task<ModDto?> GetModByNameId(ulong gameId, string modNameId, CancellationToken cancellationToken = default)
    {
        var page = await Get<PagedResultDto<ModDto>>(ModIoUrls.GetModsApiUri(GetApiKey(), gameId, nameId: modNameId), cancellationToken);
        return page?.Data.FirstOrDefault(mod => string.Equals(mod.NameId, modNameId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public Task<ModDto?> GetMod(ulong gameId, ulong modId, CancellationToken cancellationToken = default)
        => Get<ModDto>(ModIoUrls.GetModApiUri(GetApiKey(), gameId, modId), cancellationToken);

    private async Task<T?> Get<T>(Uri uri, CancellationToken cancellationToken) where T : class
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessage(response, cancellationToken);
            _logger.LogWarning("mod.io API returned {StatusCode} for `{Uri}`: {Message}", (int)response.StatusCode, Redact(uri), message);
            throw new ModIoApiException((int)response.StatusCode, message);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (result is null) _logger.LogWarning("mod.io API returned an empty body for `{Uri}`", Redact(uri));
        return result;
    }

    private static async Task<string> ReadErrorMessage(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await JsonSerializer.DeserializeAsync<ErrorEnvelopeDto>(stream, JsonOptions, cancellationToken);
            if (envelope?.Error?.Message is { Length: > 0 } message) return message;
        }
        catch (JsonException)
        {
            // non-JSON error body; fall through
        }

        return $"mod.io API request failed with status {(int)response.StatusCode}";
    }

    // api_key travels as a query parameter — never log it
    private static string Redact(Uri uri) => $"{uri.GetLeftPart(UriPartial.Path)}";
}
