using JetBrains.Annotations;
using Apocrypha.Abstractions.ModIo.DTOs;

namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// Client for the mod.io REST API (v1). Read-only api_key access: browsing and downloading
/// only, no OAuth (DESIGN-modio.md §1). All calls throw <see cref="ModIoApiKeyMissingException"/>
/// when no API key is configured and <see cref="ModIoApiException"/> on API errors.
/// </summary>
[PublicAPI]
public interface IModIoApiClient
{
    /// <summary>
    /// Resolves a game by its URL slug (e.g. <c>baldursgate3</c>); null if no such game.
    /// </summary>
    Task<GameDto?> GetGameByNameId(string gameNameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a mod by its URL slug within a game; null if no such mod.
    /// </summary>
    Task<ModDto?> GetModByNameId(ulong gameId, string modNameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single mod (embeds its latest modfile); null if no such mod.
    /// </summary>
    Task<ModDto?> GetMod(ulong gameId, ulong modId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when a mod.io API call is attempted without a configured API key.
/// </summary>
[PublicAPI]
public class ModIoApiKeyMissingException() : InvalidOperationException(
    "No mod.io API key is configured. Get a free key at mod.io/me/access and set it via the in-app dialog or `modio set-api-key`.");

/// <summary>
/// Thrown when the mod.io API returns an error response.
/// </summary>
[PublicAPI]
public class ModIoApiException(int statusCode, string message) : HttpRequestException(message)
{
    /// <summary>
    /// The HTTP status code of the error response.
    /// </summary>
    public int HttpStatusCode { get; } = statusCode;
}
