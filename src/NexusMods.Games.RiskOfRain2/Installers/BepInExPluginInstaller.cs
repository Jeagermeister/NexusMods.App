using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Thunderstore.Models;
using NexusMods.Games.RiskOfRain2.Models;
using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Library;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.RiskOfRain2.Installers;

/// <summary>
/// Installs a Thunderstore plugin package into the BepInEx folder tree, following the
/// r2modman conventions: plugin files land in <c>BepInEx/plugins/{PackageName}/</c>,
/// <c>patchers/</c> and <c>monomod/</c> get the same per-package subfolder, <c>config/</c>
/// deploys without one, and package metadata (manifest.json, README, icon) is skipped.
/// </summary>
public class BepInExPluginInstaller : ALibraryArchiveInstaller
{
    private static readonly string[] CategoryFolders = ["plugins", "patchers", "monomod", "core", "config"];

    public BepInExPluginInstaller(IServiceProvider serviceProvider)
        : base(serviceProvider, serviceProvider.GetRequiredService<ILogger<BepInExPluginInstaller>>())
    {
    }

    /// <summary>
    /// Loader packs (winhttp.dll) belong to <see cref="BepInExPackInstaller"/>; everything that
    /// looks like a Thunderstore package (metadata manifest or Thunderstore library identity)
    /// is claimed here.
    /// </summary>
    public override bool IsSupportedLibraryArchive(LibraryArchive.ReadOnly libraryArchive)
    {
        if (BepInExPackInstaller.TryFindPackRoot(libraryArchive, out _)) return false;
        if (libraryArchive.AsLibraryFile().AsLibraryItem().TryGetAsThunderstoreLibraryItem(out _)) return true;
        return libraryArchive.Children.Any(entry => entry.Path.FileName.Equals("manifest.json"));
    }

    public override ValueTask<InstallerResult> ExecuteAsync(
        LibraryArchive.ReadOnly libraryArchive,
        LoadoutItemGroup.New loadoutGroup,
        ITransaction transaction,
        Loadout.ReadOnly loadout,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedLibraryArchive(libraryArchive))
            return ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Archive is not a Thunderstore/BepInEx plugin package"));

        var packageName = GetPackageName(libraryArchive);
        var entries = libraryArchive.Children
            .Select(entry => (Entry: entry, Parts: entry.Path.ToString().Split('/')))
            .ToArray();

        // Strip a single wrapping folder when every file shares it and it isn't meaningful on its own.
        var stripRoot = false;
        var firstSegments = entries.Select(x => x.Parts[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (firstSegments.Length == 1 && entries.All(x => x.Parts.Length > 1) &&
            !IsCategoryFolder(firstSegments[0]) && !firstSegments[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
        {
            stripRoot = true;
        }

        var installedCount = 0;
        foreach (var (fileEntry, rawParts) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parts = stripRoot ? rawParts[1..] : rawParts;
            if (parts.Length == 0) continue;

            // Skip package metadata at the package root.
            if (parts.Length == 1 && InstallerHelpers.IsMetadataFileName(parts[0])) continue;

            // Normalize an explicit BepInEx/ prefix away so category rules apply either way.
            if (parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) parts = parts[1..];
            if (parts.Length == 0) continue;

            string target;
            if (parts[0].Equals("config", StringComparison.OrdinalIgnoreCase))
            {
                // Config files are shared, no per-package subfolder.
                target = $"BepInEx/config/{string.Join('/', parts[1..])}";
            }
            else if (IsCategoryFolder(parts[0]))
            {
                // plugins/patchers/monomod/core → per-package subfolder inside that category.
                target = $"BepInEx/{parts[0].ToLowerInvariant()}/{packageName}/{string.Join('/', parts[1..])}";
            }
            else
            {
                // Everything else (loose DLLs, asset folders) → the package's plugins folder.
                target = $"BepInEx/plugins/{packageName}/{string.Join('/', parts)}";
            }

            if (target.EndsWith('/')) continue;

            InstallerHelpers.AddLoadoutFile(transaction, loadout, loadoutGroup, fileEntry, new GamePath(LocationId.Game, RelativePath.FromUnsanitizedInput(target)));
            installedCount++;
        }

        if (installedCount == 0)
            return ValueTask.FromResult<InstallerResult>(new NotSupported(Reason: "Package contained no installable files"));

        _ = new BepInExPluginLoadoutItem.New(transaction, loadoutGroup.Id)
        {
            LoadoutItemGroup = loadoutGroup,
            IsPlugin = true,
        };

        return ValueTask.FromResult<InstallerResult>(new Success());
    }

    private static bool IsCategoryFolder(string segment)
        => CategoryFolders.Any(folder => folder.Equals(segment, StringComparison.OrdinalIgnoreCase));

    private static string GetPackageName(LibraryArchive.ReadOnly libraryArchive)
    {
        // Thunderstore identity gives the canonical Namespace-Name folder (r2modman convention).
        if (libraryArchive.AsLibraryFile().AsLibraryItem().TryGetAsThunderstoreLibraryItem(out var thunderstoreItem))
            return thunderstoreItem.Version.Package.FullName;

        // Fallback: archive file name without extension.
        var fileName = libraryArchive.AsLibraryFile().FileName.ToString();
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex > 0 ? fileName[..dotIndex] : fileName;
    }
}
