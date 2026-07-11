using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Thunderstore.Models;
using Apocrypha.Games.BepInEx.Models;
using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.BepInEx.Installers;

/// <summary>
/// Installs a BepInEx loader pack (e.g. Thunderstore's <c>bbepis-BepInExPack</c>) into the game
/// folder. The pack contents live under a root folder inside the archive (<c>BepInExPack/</c>
/// by convention — <c>winhttp.dll</c>, <c>doorstop_config.ini</c>, <c>BepInEx/core/…</c>) and are
/// deployed to the game root, where Unity's proxy-DLL loading picks them up.
/// </summary>
public class BepInExPackInstaller : ALibraryArchiveInstaller
{
    public BepInExPackInstaller(IServiceProvider serviceProvider)
        : base(serviceProvider, serviceProvider.GetRequiredService<ILogger<BepInExPackInstaller>>())
    {
    }

    /// <summary>
    /// A BepInEx pack is identified by the <c>winhttp.dll</c> proxy it ships.
    /// </summary>
    public override bool IsSupportedLibraryArchive(LibraryArchive.ReadOnly libraryArchive)
        => TryFindPackRoot(libraryArchive, out _);

    internal static bool TryFindPackRoot(LibraryArchive.ReadOnly libraryArchive, out string packRoot)
    {
        // The shallowest winhttp.dll marks the pack root; require a BepInEx/ folder beside it.
        var candidates = libraryArchive.Children
            .Where(entry => entry.Path.FileName.Equals("winhttp.dll"))
            .Select(entry => entry.Path.Parent.ToString())
            .OrderBy(root => root.Length)
            .ToArray();

        foreach (var root in candidates)
        {
            var bepInExPrefix = root.Length == 0 ? "BepInEx/" : $"{root}/BepInEx/";
            if (libraryArchive.Children.Any(entry => entry.Path.ToString().StartsWith(bepInExPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                packRoot = root;
                return true;
            }
        }

        packRoot = string.Empty;
        return false;
    }

    public override ValueTask<InstallerResult> ExecuteAsync(
        LibraryArchive.ReadOnly libraryArchive,
        LoadoutItemGroup.New loadoutGroup,
        ITransaction transaction,
        Loadout.ReadOnly loadout,
        CancellationToken cancellationToken)
    {
        if (!TryFindPackRoot(libraryArchive, out var packRoot))
            return ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Archive is not a BepInEx loader pack (no winhttp.dll with a BepInEx folder beside it)"));

        var installedCount = 0;
        foreach (var fileEntry in libraryArchive.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = fileEntry.Path.ToString();
            string target;
            if (packRoot.Length == 0)
            {
                // Bare pack (files at archive root): skip package metadata at the root.
                if (!path.Contains('/') && InstallerHelpers.IsMetadataFileName(fileEntry.Path.FileName))
                    continue;
                target = path;
            }
            else
            {
                // Only pack contents deploy; Thunderstore metadata beside the root folder is skipped.
                if (!path.StartsWith($"{packRoot}/", StringComparison.Ordinal)) continue;
                target = path[(packRoot.Length + 1)..];
            }

            if (target.Length == 0) continue;

            InstallerHelpers.AddLoadoutFile(transaction, loadout, loadoutGroup, fileEntry, new GamePath(LocationId.Game, RelativePath.FromUnsanitizedInput(target)));
            installedCount++;
        }

        if (installedCount == 0)
            return ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "BepInEx pack contained no installable files"));

        // Record the loader marker; version from Thunderstore metadata when available.
        var marker = new BepInExLoadoutItem.New(transaction, loadoutGroup.Id)
        {
            LoadoutItemGroup = loadoutGroup,
        };
        if (libraryArchive.AsLibraryFile().AsLibraryItem().TryGetAsThunderstoreLibraryItem(out var thunderstoreItem))
            marker.Version = thunderstoreItem.Version.VersionNumber;

        return ValueTask.FromResult<InstallerResult>(new Success());
    }
}
