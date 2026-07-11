using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Thunderstore.Models;
using Apocrypha.Games.BepInEx.Models;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.BepInEx.Schema;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.BepInEx.Installers;

/// <summary>
/// Installs a Thunderstore plugin package into the BepInEx folder tree, routed by the game's
/// ecosystem-schema <c>installRules</c> (canonical BepInEx rules when a game carries none) —
/// see <see cref="InstallRuleRouter"/>. Package metadata (manifest.json, README, icon) and the
/// game's <c>relativeFileExclusions</c> are skipped.
/// </summary>
public class BepInExPluginInstaller : ALibraryArchiveInstaller
{
    private static readonly Extension DllExtension = new(".dll");

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
    /// Per-game instance with the game's schema rules.
    /// </summary>
    public BepInExPluginInstaller(
        IServiceProvider serviceProvider,
        IReadOnlyList<EcosystemInstallRule> rules,
        IReadOnlyList<string>? relativeFileExclusions)
        : this(serviceProvider, new InstallRuleRouter(rules), relativeFileExclusions)
    {
    }

    /// <summary>
    /// Per-game instance sharing the game's router (constructed by
    /// <see cref="GenericBepInExGame"/>, which also derives its collection fallback
    /// directory from it).
    /// </summary>
    public BepInExPluginInstaller(
        IServiceProvider serviceProvider,
        InstallRuleRouter router,
        IReadOnlyList<string>? relativeFileExclusions)
        : base(serviceProvider, serviceProvider.GetRequiredService<ILogger<BepInExPluginInstaller>>())
    {
        _router = router;
        _relativeFileExclusions = relativeFileExclusions?.ToArray() ?? [];
    }

    /// <summary>
    /// Loader packs (winhttp.dll) belong to <see cref="BepInExPackInstaller"/>; everything that
    /// looks like a Thunderstore package (metadata manifest or Thunderstore library identity)
    /// or a Nexus-hosted plugin archive (a .dll anywhere, or rooted in a route folder — Nexus
    /// zips carry no manifest.json, and collections install through this path) is claimed here.
    /// </summary>
    public override bool IsSupportedLibraryArchive(LibraryArchive.ReadOnly libraryArchive)
    {
        if (BepInExPackInstaller.TryFindPackRoot(libraryArchive, out _)) return false;
        if (libraryArchive.AsLibraryFile().AsLibraryItem().TryGetAsThunderstoreLibraryItem(out _)) return true;
        return libraryArchive.Children.Any(entry =>
            entry.Path.FileName.Equals("manifest.json") ||
            entry.Path.Extension == DllExtension ||
            _router.IsRouteSegment(entry.Path.ToString().Split('/')[0]));
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
