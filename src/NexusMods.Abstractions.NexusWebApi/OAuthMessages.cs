using NexusMods.Sdk.EventBus;

namespace NexusMods.Abstractions.NexusWebApi;

/// <summary>
/// Event-bus messages for the OAuth session lifecycle.
/// </summary>
public static class OAuthMessages
{
    /// <summary>
    /// The stored Nexus Mods session could not be refreshed (rejected server-side —
    /// expired or revoked). The stale token has been dropped, so the app is now in the
    /// logged-out state; the user must log in again.
    /// </summary>
    public record SessionExpired : IEventBusMessage;
}
