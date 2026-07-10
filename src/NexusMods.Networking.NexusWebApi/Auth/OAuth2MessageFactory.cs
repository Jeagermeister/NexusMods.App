using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.Abstractions.NexusWebApi.DTOs.OAuth;
using NexusMods.Abstractions.NexusWebApi.Types;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Sdk.EventBus;

namespace NexusMods.Networking.NexusWebApi.Auth;

/// <summary>
/// OAuth2 based authentication
/// </summary>
public class OAuth2MessageFactory : BaseHttpMessageFactory, IAuthenticatingMessageFactory
{
    private readonly ILogger<OAuth2MessageFactory> _logger;
    private readonly OAuth _auth;
    private readonly IEventBus _eventBus;

    private int _sessionExpiredNotified;

    /// <summary>
    /// Constructor.
    /// </summary>
    public OAuth2MessageFactory(
        IConnection conn,
        OAuth auth,
        IEventBus eventBus,
        ILogger<OAuth2MessageFactory> logger)
    {
        _conn = conn;
        _auth = auth;
        _eventBus = eventBus;
        _logger = logger;
    }

    private readonly IConnection _conn;

    /// <inheritdoc/>
    public override AuthenticationHeaderValue? GetAuthenticationHeaderValue()
    {
        var token = GetToken();
        if (token is null) return null;

        return new AuthenticationHeaderValue("Bearer", token);
    }

    private string? GetToken()
    {
        if (!JWTToken.TryFind(_conn.Db, out var token)) return null;
        if (!token.HasExpired) return token.AccessToken;

        return null;
    }

    private async ValueTask<string?> GetOrRefreshToken(CancellationToken cancellationToken)
    {
        if (!JWTToken.TryFind(_conn.Db, out var token)) return null;
        if (!token.HasExpired)
        {
            // A live session means any previously-notified expiry has been resolved by a re-login.
            Interlocked.Exchange(ref _sessionExpiredNotified, 0);
            return token.AccessToken;
        }

        _logger.LogDebug("Refreshing expired OAuth token");

        JwtTokenReply? newToken;
        try
        {
            newToken = await _auth.RefreshToken(token.RefreshToken, cancellationToken);
        }
        catch (OAuthSessionExpiredException)
        {
            await HandleSessionExpired();
            return null;
        }
        var db = _conn.Db;
        using var tx = _conn.BeginTransaction();

        var newTokenEntity = JWTToken.Create(db, tx, newToken!);
        if (!newTokenEntity.HasValue)
        {
            _logger.LogError("Invalid new token in OAuth2MessageFactory");
            return null;
        }

        var result = await tx.Commit();

        token = JWTToken.Load(result.Db, result[newTokenEntity.Value]);
        return token.AccessToken;
    }

    /// <summary>
    /// The refresh token was rejected server-side: the session is dead, and every future
    /// request would fail the same way. Drop the stored token (mirrors
    /// <c>LoginManager.Logout</c> — retract so the UI flips to logged-out, then excise the
    /// secrets) and tell the user once.
    /// </summary>
    private async Task HandleSessionExpired()
    {
        _logger.LogWarning("Nexus Mods session has expired and could not be refreshed — user must log in again");

        var tokenEntities = JWTToken.All(_conn.Db).Select(e => e.Id).ToArray();
        if (tokenEntities.Length > 0)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var entity in tokenEntities)
                tx.Delete(entity, recursive: false);
            await tx.Commit();

            await _conn.Excise(tokenEntities);
        }

        if (Interlocked.Exchange(ref _sessionExpiredNotified, 1) == 0)
            _eventBus.Send(new OAuthMessages.SessionExpired());
    }

    /// <inheritdoc/>
    public override async ValueTask<HttpRequestMessage> Create(HttpMethod method, Uri uri)
    {
        var token = await GetOrRefreshToken(CancellationToken.None);
        if (token is null) throw new Exception("Unauthorized!");

        var requestMessage = await base.Create(method, uri);
        requestMessage.Headers.Add("Authorization", $"Bearer {token}");
        return requestMessage;
    }

    /// <inheritdoc/>
    public override async ValueTask<bool> IsAuthenticated()
    {
        var token = await GetOrRefreshToken(CancellationToken.None);
        return token is not null;
    }

    /// <inheritdoc/>
    public async ValueTask<UserInfo?> Verify(INexusApiClient nexusApiNexusApiClient, CancellationToken cancel)
    {
        OAuthUserInfo oAuthUserInfo;
        try
        {
            var res = await nexusApiNexusApiClient.GetOAuthUserInfo(cancellationToken: cancel);
            oAuthUserInfo = res.Data;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception while fetching OAuth user info");
            return null;
        }

        return new UserInfo
        {
            UserId = UserId.From(ulong.Parse(oAuthUserInfo.Sub)),
            Name = oAuthUserInfo.Name,
            UserRole =  oAuthUserInfo.MembershipRoles.Contains(MembershipRole.Premium) ? UserRole.Premium : oAuthUserInfo.MembershipRoles.Contains(MembershipRole.Supporter) ? UserRole.Supporter : UserRole.Free,
            AvatarUrl = oAuthUserInfo.Avatar,
        };
    }
}
