using System.Collections.Immutable;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Games.RiskOfRain2.Emitters;
using NexusMods.Games.RiskOfRain2.Installers;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;

namespace NexusMods.Games.RiskOfRain2;

/// <summary>
/// Risk of Rain 2 — the fork's pilot for the Thunderstore/BepInEx mod-source work
/// (DESIGN-modsources.md §8). Notably it has NO Nexus Mods presence: modding is
/// Thunderstore-exclusive, making it the first game this app supports that the
/// upstream app never could.
/// </summary>
[UsedImplicitly]
public class RiskOfRain2Game : IGame, IGameData<RiskOfRain2Game>
{
    public static GameId GameId { get; } = GameId.From("RiskOfRain2");
    public static string DisplayName => "Risk of Rain 2";

    /// <remarks>Thunderstore-exclusive; the game is not on Nexus Mods.</remarks>
    public static Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => Optional<Sdk.NexusModsApi.NexusModsGameId>.None;

    public StoreIdentifiers StoreIdentifiers { get; } = new(GameId)
    {
        SteamAppIds = [632360u],
    };

    public IStreamFactory IconImage { get; } = new EmbeddedResourceStreamFactory<RiskOfRain2Game>("NexusMods.Games.RiskOfRain2.Resources.thumbnail.webp");
    public IStreamFactory TileImage { get; } = new EmbeddedResourceStreamFactory<RiskOfRain2Game>("NexusMods.Games.RiskOfRain2.Resources.tile.webp");

    private readonly Lazy<ILoadoutSynchronizer> _synchronizer;
    public ILoadoutSynchronizer Synchronizer => _synchronizer.Value;
    public ILibraryItemInstaller[] LibraryItemInstallers { get; }
    private readonly Lazy<ISortOrderManager> _sortOrderManager;
    public ISortOrderManager SortOrderManager => _sortOrderManager.Value;
    public IDiagnosticEmitter[] DiagnosticEmitters { get; }

    public RiskOfRain2Game(IServiceProvider provider)
    {
        _synchronizer = new Lazy<ILoadoutSynchronizer>(() => new DefaultSynchronizer(provider));
        _sortOrderManager = new Lazy<ISortOrderManager>(() =>
        {
            var sortOrderManager = provider.GetRequiredService<SortOrderManager>();
            sortOrderManager.RegisterSortOrderVarieties([], this);
            return sortOrderManager;
        });

        DiagnosticEmitters =
        [
            provider.GetRequiredService<MissingBepInExEmitter>(),
        ];

        LibraryItemInstallers =
        [
            provider.GetRequiredService<BepInExPackInstaller>(),
            provider.GetRequiredService<BepInExPluginInstaller>(),
        ];
    }

    public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult)
    {
        return new Dictionary<LocationId, AbsolutePath>
        {
            { LocationId.Game, gameLocatorResult.Path },
        }.ToImmutableDictionary();
    }

    public GamePath GetPrimaryFile(GameInstallation installation) => new(LocationId.Game, "Risk of Rain 2.exe");
}
