using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Sdk.Settings;
using NexusMods.Paths;
using NexusMods.Sdk;

namespace NexusMods.Games.FileHashes;

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
    /// Linux fork: disabled by default. The upstream feed (github.com/Nexus-Mods/game-hashes) is
    /// frozen and unmaintained, so polling it only produces error-log noise and never yields
    /// updates. With this off, the app relies solely on the local database or the embedded
    /// snapshot shipped with the build, and makes zero calls to Nexus infrastructure at runtime.
    /// TODO(linux-fork): re-enable and repoint <see cref="GithubManifestUrl"/> /
    /// <see cref="GameHashesDbUrl"/> at the fork's own hash pipeline once it is live.
    /// </remarks>
    public bool EnableRemoteUpdates { get; init; } = false;

    /// <summary>
    /// Only checks GitHub for updates this often, in order to avoid API rate limits.
    /// </summary>
    public TimeSpan HashDatabaseUpdateInterval { get; init; } = TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// The URL to the Github API to get the latest release.
    /// </summary>
    public Uri GithubManifestUrl { get; init; } = new("https://github.com/Nexus-Mods/game-hashes/releases/latest/download/manifest.json");
    
    /// <summary>
    /// The URL to the GitHub release to download the latest hashes database.
    /// </summary>
    public Uri GameHashesDbUrl { get; init; } = new("https://github.com/Nexus-Mods/game-hashes/releases/latest/download/game_hashes_db.zip");
    
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
