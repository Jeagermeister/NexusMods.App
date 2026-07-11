using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.BepInEx.Installers;

internal static class InstallerHelpers
{
    /// <summary>
    /// Package-metadata files that Thunderstore requires at package root but which must not
    /// be deployed into the game folder.
    /// </summary>
    public static readonly string[] MetadataFileNames = ["manifest.json", "README.md", "CHANGELOG.md", "icon.png", "LICENSE", "LICENSE.md"];

    public static bool IsMetadataFileName(string fileName)
        => MetadataFileNames.Any(name => name.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Creates a loadout file for an archive entry at the given target game path
    /// (the standard installer idiom, mirroring SMAPIInstaller).
    /// </summary>
    public static void AddLoadoutFile(
        ITransaction transaction,
        Loadout.ReadOnly loadout,
        LoadoutItemGroup.New loadoutGroup,
        LibraryArchiveFileEntry.ReadOnly fileEntry,
        GamePath to)
    {
        var entityId = transaction.TempId();
        var loadoutItem = new LoadoutItem.New(transaction, entityId)
        {
            Name = fileEntry.Path.FileName,
            LoadoutId = loadout,
            ParentId = loadoutGroup,
        };

        _ = new LoadoutFile.New(transaction, entityId)
        {
            Hash = fileEntry.AsLibraryFile().Hash,
            Size = fileEntry.AsLibraryFile().Size,
            LoadoutItemWithTargetPath = new LoadoutItemWithTargetPath.New(transaction, entityId)
            {
                TargetPath = to.ToGamePathParentTuple(loadout.Id),
                LoadoutItem = loadoutItem,
            },
        };
    }
}
