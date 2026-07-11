using JetBrains.Annotations;

namespace Apocrypha.Sdk;

/// <summary>
/// The app's OS-facing identity: the per-user data directory base name and the reverse-DNS
/// application id. This is the ONE definition — every path derivation and OS registration
/// must consume these constants (rebrand R3 unified the previously independent copies;
/// KIRO-HANDOFF.md §23.2/§25.1).
/// </summary>
[PublicAPI]
public static class ApplicationIdentity
{
    /// <summary>
    /// Base directory name for all per-user app data (DataModel, Configs, Logs, Temp, Cache,
    /// auth).
    /// </summary>
    public const string DataDirectoryName = "Apocrypha";

    /// <summary>
    /// Reverse-DNS application id: desktop-file/AppStream id, xdg protocol registration.
    /// </summary>
    public const string AppId = "io.github.jeagermeister.apocrypha";

    /// <summary>
    /// Pre-rebrand data dir base name (Windows/Linux). Consumed only by
    /// <see cref="LegacyDataMigration"/> and old-registration cleanup.
    /// </summary>
    public const string LegacyDataDirectoryName = "NexusMods.App";

    /// <summary>Pre-rebrand reverse-DNS id. Consumed only by old-registration cleanup.</summary>
    public const string LegacyAppId = "com.nexusmods.app";
}
