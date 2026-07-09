using NexusMods.Paths;

namespace NexusMods.Games.BepInEx;

/// <summary>
/// Resolves where the family's downloaded game art lives on disk (design §10):
/// <c>{app data dir}/Cache/GameArt/{asset file name}</c>. Everything under it is a pure
/// cache — safe to delete, re-downloaded on demand.
/// </summary>
internal static class GameArtCache
{
    /// <param name="fileSystem">Filesystem the cache lives on.</param>
    /// <param name="gcdnPath">Asset path relative to the gcdn assets root, e.g. <c>subnautica/subnautica-cover-360x480.webp</c>.</param>
    public static AbsolutePath GetCacheFile(IFileSystem fileSystem, string gcdnPath)
    {
        // gcdn file names embed the community slug, so the flattened name stays unique.
        var fileName = gcdnPath[(gcdnPath.LastIndexOf('/') + 1)..];
        return GetCacheDirectory(fileSystem).Combine(fileName);
    }

    // TODO(linux-fork): rebrand R3 unifies the app's independently-derived base-dir names
    // (KIRO-HANDOFF.md §23.2) — fold this copy into the shared constant then. Being a pure
    // cache, the R3 move-migration can skip this directory.
    private static AbsolutePath GetCacheDirectory(IFileSystem fileSystem)
    {
        var os = fileSystem.OS;
        var baseKnownPath = os.MatchPlatform(
            onWindows: () => KnownPath.LocalApplicationDataDirectory,
            onLinux: () => KnownPath.XDG_DATA_HOME,
            onOSX: () => KnownPath.LocalApplicationDataDirectory
        );

        var baseDirectoryName = os.IsOSX ? "NexusMods_App" : "NexusMods.App";
        return fileSystem.GetKnownPath(baseKnownPath).Combine(baseDirectoryName).Combine("Cache/GameArt");
    }
}
