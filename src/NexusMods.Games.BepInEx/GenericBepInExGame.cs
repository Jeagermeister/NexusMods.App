using System.Collections.Immutable;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Games.BepInEx.Emitters;
using NexusMods.Games.BepInEx.Installers;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;

namespace NexusMods.Games.BepInEx;

/// <summary>
/// One Thunderstore/BepInEx game, constructed from a <see cref="BepInExGameData"/> row of the
/// vendored ecosystem schema — the data-driven family that generalizes the Phase 1 RoR2 pilot
/// (DESIGN-bepinex-family.md §5). Deliberately implements plain <see cref="IGame"/>, not
/// <c>IGameData&lt;TSelf&gt;</c>: the static-abstract members are a per-class convention, and this
/// class is instantiated once per game row.
/// </summary>
public class GenericBepInExGame : IGame
{
    private readonly BepInExGameData _data;

    public BepInExGameData Data => _data;

    public GameId GameId => _data.GameId;
    public string DisplayName => _data.DisplayName;
    public Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => _data.NexusModsGameId;

    public StoreIdentifiers StoreIdentifiers { get; }

    /// <remarks>
    /// Shared placeholder art until the runtime art pipeline (design §10, PR H') serves the
    /// schema's per-game covers.
    /// </remarks>
    public IStreamFactory IconImage { get; } = new EmbeddedResourceStreamFactory<GenericBepInExGame>("NexusMods.Games.BepInEx.Resources.thumbnail.webp");
    public IStreamFactory TileImage { get; } = new EmbeddedResourceStreamFactory<GenericBepInExGame>("NexusMods.Games.BepInEx.Resources.tile.webp");

    private readonly Lazy<ILoadoutSynchronizer> _synchronizer;
    public ILoadoutSynchronizer Synchronizer => _synchronizer.Value;
    public ILibraryItemInstaller[] LibraryItemInstallers { get; }
    private readonly Lazy<ISortOrderManager> _sortOrderManager;
    public ISortOrderManager SortOrderManager => _sortOrderManager.Value;
    public IDiagnosticEmitter[] DiagnosticEmitters { get; }

    public GenericBepInExGame(IServiceProvider provider, BepInExGameData data)
    {
        _data = data;
        StoreIdentifiers = new StoreIdentifiers(data.GameId)
        {
            SteamAppIds = data.SteamAppIds,
        };

        _synchronizer = new Lazy<ILoadoutSynchronizer>(() => new DefaultSynchronizer(provider));
        _sortOrderManager = new Lazy<ISortOrderManager>(() =>
        {
            var sortOrderManager = provider.GetRequiredService<SortOrderManager>();
            sortOrderManager.RegisterSortOrderVarieties([], this);
            return sortOrderManager;
        });

        DiagnosticEmitters =
        [
            new MissingBepInExEmitter(data.CommunitySlug),
        ];

        LibraryItemInstallers =
        [
            provider.GetRequiredService<BepInExPackInstaller>(),
            // Per-game instance: routing follows this game's schema installRules (design §6).
            new BepInExPluginInstaller(provider, data.InstallRules, data.RelativeFileExclusions),
        ];
    }

    public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult)
    {
        return new Dictionary<LocationId, AbsolutePath>
        {
            { LocationId.Game, gameLocatorResult.Path },
        }.ToImmutableDictionary();
    }

    public GamePath GetPrimaryFile(GameInstallation installation) => new(LocationId.Game, _data.PrimaryExeName);
}
