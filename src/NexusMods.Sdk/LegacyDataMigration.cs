using JetBrains.Annotations;
using NexusMods.Paths;

namespace NexusMods.Sdk;

/// <summary>
/// One-time rebrand migration (R3): moves the per-user data directories from the pre-rebrand
/// base name (<c>NexusMods.App</c>) to
/// <see cref="ApplicationIdentity.DataDirectoryName"/>, then rewrites the legacy path
/// fragments persisted inside the settings JSON files (ConfigurablePath <c>File</c> values
/// and the CLI sync-file path pin the OLD relative paths — without the rewrite the app would
/// resolve the moved-away locations and strand the user's loadouts/Library).
/// </summary>
/// <remarks>
/// MUST run at the very top of <c>Program.Main</c>, before ANYTHING touches the data
/// directories: the settings host creates the Configs dir, logging creates the state dir,
/// MnemonicDB opens the database. Idempotent and crash-safe: each directory move is a single
/// atomic same-volume rename gated on "old exists and new doesn't" (true at most once), and
/// the config rewrite re-scans on every run, so a crash between move and rewrite self-heals
/// on the next start. Assumes no other app instance is running (single-user desktop flow —
/// the single-process guard itself only comes up after settings load).
/// </remarks>
[PublicAPI]
public static class LegacyDataMigration
{
    /// <summary>
    /// Runs the migration. Returns true when anything was moved or rewritten.
    /// </summary>
    /// <param name="fileSystem">Filesystem to operate on (overlay-mapped in tests).</param>
    /// <param name="report">Sink for progress messages; logging is not up yet at the call site.</param>
    public static bool Run(IFileSystem fileSystem, Action<string> report)
    {
        var os = fileSystem.OS;
        var didWork = false;

        // Resolved-path dedup: on Linux LocalApplicationDataDirectory and XDG_DATA_HOME
        // usually resolve to the same ~/.local/share.
        var baseDirectories = GetBaseDirectories(os)
            .Select(knownPath => fileSystem.GetKnownPath(knownPath))
            .Distinct()
            .ToArray();

        foreach (var baseDirectory in baseDirectories)
        {
            var oldDirectory = baseDirectory.Combine(ApplicationIdentity.LegacyDataDirectoryName);
            var newDirectory = baseDirectory.Combine(ApplicationIdentity.DataDirectoryName);
            if (!oldDirectory.DirectoryExists()) continue;

            if (newDirectory.DirectoryExists())
            {
                report($"Rebrand migration: both `{oldDirectory}` and `{newDirectory}` exist — leaving the old directory untouched");
                continue;
            }

            // One atomic same-volume rename — instant regardless of size, and a crash
            // can't leave a half-moved tree.
            System.IO.Directory.Move(oldDirectory.ToString(), newDirectory.ToString());
            report($"Rebrand migration: moved `{oldDirectory}` -> `{newDirectory}`");
            didWork = true;
        }

        foreach (var baseDirectory in baseDirectories)
        {
            var configsDirectory = baseDirectory.Combine(ApplicationIdentity.DataDirectoryName).Combine("Configs");
            if (!configsDirectory.DirectoryExists()) continue;

            foreach (var configFile in configsDirectory.EnumerateFiles(pattern: "*.json", recursive: false))
            {
                var contents = configFile.ReadAllTextAsync().GetAwaiter().GetResult();
                var rewritten = RewriteLegacyFragments(contents);
                if (rewritten == contents) continue;

                configFile.WriteAllTextAsync(rewritten).GetAwaiter().GetResult();
                report($"Rebrand migration: rewrote legacy path fragments in `{configFile.Name}`");
                didWork = true;
            }
        }

        return didWork;
    }

    /// <summary>
    /// Rewrites the legacy path fragments a persisted settings JSON can contain: relative
    /// ConfigurablePath directory prefixes, the CLI sync-file name, and the log file base
    /// names (all path-shaped values serialize with forward slashes on every platform).
    /// </summary>
    public static string RewriteLegacyFragments(string json)
    {
        return json
            .Replace($"{ApplicationIdentity.LegacyDataDirectoryName}/", $"{ApplicationIdentity.DataDirectoryName}/", StringComparison.Ordinal)
            .Replace($"{ApplicationIdentity.LegacyDataDirectoryName}-sync_file", $"{ApplicationIdentity.DataDirectoryName}-sync_file", StringComparison.Ordinal)
            .Replace("nexusmods.app.main", "apocrypha.main", StringComparison.Ordinal)
            .Replace("nexusmods.app.slim", "apocrypha.slim", StringComparison.Ordinal);
    }

    private static KnownPath[] GetBaseDirectories(IOSInformation os) => os.MatchPlatform<KnownPath[]>(
        onWindows: () => [KnownPath.LocalApplicationDataDirectory],
        onLinux: () => [KnownPath.XDG_DATA_HOME, KnownPath.XDG_STATE_HOME, KnownPath.LocalApplicationDataDirectory]
    );
}
