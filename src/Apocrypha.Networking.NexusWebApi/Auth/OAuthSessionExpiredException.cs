namespace Apocrypha.Networking.NexusWebApi.Auth;

/// <summary>
/// Thrown when the OAuth backend rejects a token refresh outright (4xx — the session is
/// expired or revoked). Distinct from transient failures (network, 5xx), which surface as
/// <see cref="HttpRequestException"/>: only this exception means the user must log in again.
/// </summary>
public class OAuthSessionExpiredException : Exception
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public OAuthSessionExpiredException(string message) : base(message) { }
}
