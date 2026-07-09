using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Thunderstore.Models;
using NexusMods.Games.BepInEx.Models;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.BepInEx.Schema;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Library;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.BepInEx.Installers;

/// <summary>
/// Installs a Thunderstore plugin package into the BepInEx folder tree, routed by the game's
/// ecosystem-schema <c>installRules</c> (canonical BepInEx rules when a game carries none) —
/// see <see cref="InstallRuleRouter"/>. Package metadata (manifest.json, README, icon) and the
/// game's <c>relativeFileExclusions</c> are skipped.
/// </summary>
public class BepInExPluginInstaller : ALibraryArchiveInstaller
{
    private readonly InstallRuleRouter _router;
    private readonly string[] _relativeFileExclusions;

    /// <summary>
    /// Canonical-rules instance (the DI singleton the hand-written RoR2 module resolves).
    /// </summary>
    public BepInExPluginInstaller(IServiceProvider serviceProvider)
        : this(serviceProvider, rules: [], relativeFileExclusions: null)
    {
    }

    /// <summary>
    /// Per-game instance with the game's schema rules (constructed by
    /// <see cref="GenericBepInExGame"/>).
    /// </summary>
    public BepInExPluginInstaller(
        IServiceProvider serviceProvider,
        IReadOnlyList<EcosystemInstallRule> rules,
        IReadOnlyList<string>? relativeFileExclusions)
        : base(serviceProvider, serviceProvider.GetRequiredService<ILogger<BepInExPluginInstaller>>())
    {
        _router = new InstallRuleRouter(rules);
        _relativeFileExclusions = relativeFileExclusions?.ToArray() ?? [];
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

        // Strip a single wrapping folder when every file shares it and it isn't meaningful on
        // its own (a route folder like QMods/ or BepInEx/ must survive).
        var stripRoot = false;
        var firstSegments = entries.Select(x => x.Parts[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (firstSegments.Length == 1 && entries.All(x => x.Parts.Length > 1) && !_router.IsRouteSegment(firstSegments[0]))
        {
            stripRoot = true;
        }

        var installedCount = 0;
        foreach (var (fileEntry, rawParts) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parts = stripRoot ? rawParts[1..] : rawParts;
            if (parts.Length == 0) continue;
            if (IsExcluded(parts)) continue;

            var target = _router.Route(parts, packageName);
            if (target.ToString().Length == 0) continue;

            InstallerHelpers.AddLoadoutFile(transaction, loadout, loadoutGroup, fileEntry, new GamePath(LocationId.Game, target));
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

    private bool IsExcluded(string[] parts)
    {
        // Package metadata at the package root.
        if (parts.Length == 1 && InstallerHelpers.IsMetadataFileName(parts[0])) return true;

        // The game's schema exclusions: root file names or full relative paths.
        if (_relativeFileExclusions.Length == 0) return false;
        var relativePath = string.Join('/', parts);
        return _relativeFileExclusions.Any(exclusion =>
            exclusion.Equals(relativePath, StringComparison.OrdinalIgnoreCase) ||
            (parts.Length == 1 && exclusion.Equals(parts[0], StringComparison.OrdinalIgnoreCase)));
    }

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
