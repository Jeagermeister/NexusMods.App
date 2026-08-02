
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Games.CreationEngine;

public static class KnownPaths
{
    public static readonly GamePath Game = new(LocationId.Game, "");
    public static readonly GamePath Data = new(LocationId.Game, "Data");
    public static readonly GamePath SKSE64Loader = new(LocationId.Game, "skse64_loader.exe");
    public static readonly GamePath F4SELoader = new(LocationId.Game, "f4se_loader.exe");

    /// <summary>
    /// The <c>My Games</c> directory, surviving a Linux box with no <c>Documents</c> directory
    /// (review finding B-4).
    ///
    /// <para>
    /// On Linux, <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> resolves
    /// <c>MyDocuments</c> to the empty string when the directory does not <em>exist</em> —
    /// headless boxes and minimal setups that never ran <c>xdg-user-dirs</c>. NexusMods.Paths
    /// then hands back a relative junk value (verified: <c>'My Games'</c>, not an exception and
    /// with <c>HasKnownPath</c> still true), and it is the first <c>/</c> combine in
    /// <c>GetLocations</c> that throws <c>PathException</c>. <c>GameRegistry</c> catches
    /// per-game and logs, so Fallout 4 and Skyrim SE silently never register.
    /// </para>
    ///
    /// <para>
    /// The fallback is <c>$HOME/Documents/My Games</c> — the same path a healthy box resolves —
    /// so a user who later creates <c>~/Documents</c> keeps the same location. The directory is
    /// deliberately not created here: the synchronizer creates it at first write, exactly like a
    /// fresh game install would. The fallback must NOT move into the shared FileSystem:
    /// Proton-prefix games arrive here with an overlay FileSystem whose known paths map into the
    /// prefix (rooted, so the primary branch is taken), and that redirection has to keep its
    /// semantics.
    /// </para>
    /// </summary>
    public static AbsolutePath MyGamesOrFallback(IFileSystem fileSystem)
    {
        var myGames = fileSystem.GetKnownPath(KnownPath.MyGamesDirectory);
        if (PathHelpers.IsRooted(myGames.ToString())) return myGames;
        return fileSystem.GetKnownPath(KnownPath.HomeDirectory) / "Documents" / "My Games";
    }
    
    /// <summary>
    /// Common top level folders for use in the StopPatternInstaller.
    /// </summary>
    public static readonly string[] CommonTopLevelFolders =
    [
        "asi",
        "calientetools",
        "distantlod",
        "facegen",
        "fonts",
        "interface",
        "lodsettings",
        "lsdata",
        "menus",
        "meshes",
        "music",
        "scripts",
        "shaders",
        "sound",
        "strings",
        "textures",
        "tools",
        "trees",
        "video",
    ];
}
