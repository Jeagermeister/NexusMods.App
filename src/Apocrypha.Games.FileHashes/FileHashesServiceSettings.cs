using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Sdk.Settings;
using NexusMods.Paths;
using Apocrypha.Sdk;

namespace Apocrypha.Games.FileHashes;

[PublicAPI]
public record FileHashesServiceSettings : ISettings
{
    /// <summary>
    /// Location where the temporary folder will be stored.
    /// </summary>
    public ConfigurablePath HashDatabaseLocation { get; init; }
    
    /// <summary>
    /// When <c>true</c>, the service polls a remote feed (<see cref="GithubManifestUrl"/> /
    /// <see cref="GameHashesDbUrl"/>) for an updated hashes database at runtime.
    /// </summary>
    /// <remarks>
    /// Linux fork: points at the fork-owned feed (github.com/Jeagermeister/game-hashes — a fork of
    /// the upstream Nexus-Mods/game-hashes, which froze 2025-09-30), refreshed manually via
    /// <c>steam app index</c> → <c>game-hashes-db build</c> → a GitHub release (see
    /// HASHES-RUNBOOK.md in the notes repo). Polling makes zero calls to Nexus infrastructure —
    /// only to the fork's GitHub releases. On any fetch failure the service falls back to the
    /// newest local database or the embedded snapshot, so an outage never breaks the app.
    /// </remarks>
    public bool EnableRemoteUpdates { get; init; } = true;

    /// <summary>
    /// Only checks GitHub for updates this often, in order to avoid API rate limits.
    /// </summary>
    public TimeSpan HashDatabaseUpdateInterval { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The URL of the latest release's manifest on the fork-owned feed.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>static</c> (not a serialized setting): these URLs are infrastructure, not
    /// user preferences — no UI exposes them. They used to be instance properties, which meant any
    /// install with a persisted <c>FileHashesServiceSettings.json</c> (written before the fork
    /// repointed the feed) kept polling the frozen upstream URLs forever, silently overriding the
    /// new defaults. Static members are ignored by the JSON settings backend, so old persisted
    /// values can no longer pin the feed.
    /// </remarks>
    public static Uri GithubManifestUrl => new("https://github.com/Jeagermeister/game-hashes/releases/latest/download/manifest.json");

    /// <summary>
    /// The URL of the latest release's hashes database on the fork-owned feed.
    /// </summary>
    /// <remarks><inheritdoc cref="GithubManifestUrl"/></remarks>
    public static Uri GameHashesDbUrl => new("https://github.com/Jeagermeister/game-hashes/releases/latest/download/game_hashes_db.zip");
    
    public RelativePath ReleaseFilePath { get; init; } = "game-hashes-release.json";
    

    /// <inheritdoc/>
    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder
            .ConfigureDefault(CreateDefault)
            .ConfigureBackend(StorageBackendOptions.Use(StorageBackends.Json));
    }

    /// <summary>
    /// Create default.
    /// </summary>
    public static FileHashesServiceSettings CreateDefault(IServiceProvider serviceProvider)
    {
        var os = serviceProvider.GetRequiredService<IFileSystem>().OS;

        // Note: The idiomatic place for this is Temporary Directory (/tmp on Linux, %TEMP% on Windows)
        //       however this can be dangerous to do on Linux, as /tmp is often a RAM disk, and can be
        //       too small to handle large files.
        var baseKnownPath = os.MatchPlatform(
            onWindows: () => KnownPath.LocalApplicationDataDirectory,
            onLinux: () => KnownPath.XDG_DATA_HOME
        );

        return new FileHashesServiceSettings
        {
            HashDatabaseLocation = new ConfigurablePath(baseKnownPath, $"{ApplicationIdentity.DataDirectoryName}/FileHashesDatabase"),
        };
    }
}
