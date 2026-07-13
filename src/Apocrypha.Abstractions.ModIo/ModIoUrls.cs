using System.Diagnostics.CodeAnalysis;

namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// Builders and parsers for the (stable, documented) mod.io URL shapes. Lives in
/// abstractions so UI surfaces can link mod.io pages without the API client.
/// </summary>
public static class ModIoUrls
{
    private const string SiteBaseUrl = "https://mod.io";
    private const string ApiBaseUrl = "https://api.mod.io/v1";

    /// <summary>
    /// Where a user gets their free API key.
    /// </summary>
    public static readonly Uri ApiKeyPageUri = new("https://mod.io/me/access");

    /// <summary>
    /// A game's browse page on mod.io.
    /// </summary>
    public static Uri GetGamePageUri(string gameNameId)
        => new($"{SiteBaseUrl}/g/{Uri.EscapeDataString(gameNameId)}");

    /// <summary>
    /// A mod's page on mod.io.
    /// </summary>
    public static Uri GetModPageUri(string gameNameId, string modNameId)
        => new($"{SiteBaseUrl}/g/{Uri.EscapeDataString(gameNameId)}/m/{Uri.EscapeDataString(modNameId)}");

    /// <summary>
    /// The API endpoint listing games, optionally filtered by <c>name_id</c>.
    /// </summary>
    public static Uri GetGamesApiUri(string apiKey, string? nameId = null)
        => new($"{ApiBaseUrl}/games?api_key={Uri.EscapeDataString(apiKey)}{(nameId is null ? "" : $"&name_id={Uri.EscapeDataString(nameId)}")}");

    /// <summary>
    /// The API endpoint listing a game's mods, optionally filtered by <c>name_id</c>.
    /// </summary>
    public static Uri GetModsApiUri(string apiKey, ulong gameId, string? nameId = null)
        => new($"{ApiBaseUrl}/games/{gameId}/mods?api_key={Uri.EscapeDataString(apiKey)}{(nameId is null ? "" : $"&name_id={Uri.EscapeDataString(nameId)}")}");

    /// <summary>
    /// The API endpoint for a single mod (embeds its latest modfile).
    /// </summary>
    public static Uri GetModApiUri(string apiKey, ulong gameId, ulong modId)
        => new($"{ApiBaseUrl}/games/{gameId}/mods/{modId}?api_key={Uri.EscapeDataString(apiKey)}");

    /// <summary>
    /// Parses a mod.io mod page link (<c>https://mod.io/g/{game}/m/{mod}</c>, with or
    /// without <c>www.</c>, trailing slashes or extra path segments after the mod slug).
    /// </summary>
    public static bool TryParseModUrl(string? input, [NotNullWhen(true)] out string? gameNameId, [NotNullWhen(true)] out string? modNameId)
    {
        gameNameId = null;
        modNameId = null;

        if (string.IsNullOrWhiteSpace(input)) return false;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (!uri.Host.Equals("mod.io", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("www.mod.io", StringComparison.OrdinalIgnoreCase)) return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4) return false;
        if (!segments[0].Equals("g", StringComparison.OrdinalIgnoreCase)) return false;
        if (!segments[2].Equals("m", StringComparison.OrdinalIgnoreCase)) return false;
        if (segments[1].Length == 0 || segments[3].Length == 0) return false;

        gameNameId = segments[1];
        modNameId = segments[3];
        return true;
    }
}
