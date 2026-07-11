using System.Collections.Immutable;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.MountAndBlade2Bannerlord.Diagnostics;
using Apocrypha.Games.MountAndBlade2Bannerlord.Installers;
using Apocrypha.Games.MountAndBlade2Bannerlord.Utils;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.IO;
using static Apocrypha.Games.MountAndBlade2Bannerlord.BannerlordConstants;

namespace Apocrypha.Games.MountAndBlade2Bannerlord;

/// <summary>
/// Maintained by the BUTR Team
/// https://github.com/BUTR
/// </summary>
public sealed class Bannerlord : IGame, IGameData<Bannerlord>
{
    public static GameId GameId { get; } = GameId.From("Bannerlord");
    public static string DisplayName => "Mount & Blade II: Bannerlord";
    public static Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => Sdk.NexusModsApi.NexusModsGameId.From(3174);

    public StoreIdentifiers StoreIdentifiers { get; } = new(GameId)
    {
        SteamAppIds = [261550u],
        GOGProductIds = [1802539526L, 1564781494L],
        EGSCatalogItemId = ["Chickadee"],
        XboxPackageIdentifiers = ["TaleWorldsEntertainment.MountBladeIIBannerlord"],
    };

    public IStreamFactory IconImage { get; } = new EmbeddedResourceStreamFactory<Bannerlord>("Apocrypha.Games.MountAndBlade2Bannerlord.Resources.thumbnail.webp");
    public IStreamFactory TileImage { get; } = new EmbeddedResourceStreamFactory<Bannerlord>("Apocrypha.Games.MountAndBlade2Bannerlord.Resources.tile.webp");

    private readonly Lazy<ILoadoutSynchronizer> _synchronizer;
    public ILoadoutSynchronizer Synchronizer => _synchronizer.Value;
    public ILibraryItemInstaller[] LibraryItemInstallers { get; }
    private readonly Lazy<ISortOrderManager> _sortOrderManager;
    public ISortOrderManager SortOrderManager => _sortOrderManager.Value;
    public IDiagnosticEmitter[] DiagnosticEmitters { get; }

    public Bannerlord(IServiceProvider serviceProvider)
    {
        _synchronizer = new Lazy<ILoadoutSynchronizer>(() => new BannerlordLoadoutSynchronizer(serviceProvider));
        _sortOrderManager = new Lazy<ISortOrderManager>(() =>
        {
            var sortOrderManager = serviceProvider.GetRequiredService<SortOrderManager>();
            sortOrderManager.RegisterSortOrderVarieties([], this);
            return sortOrderManager;
        });

        DiagnosticEmitters =
        [
            new BannerlordDiagnosticEmitter(serviceProvider),
            new MissingProtontricksEmitter(serviceProvider),
        ];

        LibraryItemInstallers =
        [
            serviceProvider.GetRequiredService<BLSEInstaller>(),
            serviceProvider.GetRequiredService<BannerlordModInstaller>(),
        ];
    }

    public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult)
    {
        var documentsFolder = fileSystem.GetKnownPath(KnownPath.MyDocumentsDirectory);
        return new Dictionary<LocationId, AbsolutePath>
        {
            { LocationId.Game, gameLocatorResult.Store == GameStore.XboxGamePass ? gameLocatorResult.Path.Combine("Content") : gameLocatorResult.Path },
            { LocationId.Saves, documentsFolder.Combine($"{DocumentsFolderName}/Game Saves") },
            { LocationId.Preferences, documentsFolder.Combine($"{DocumentsFolderName}/Configs") },
        }.ToImmutableDictionary();
    }

    public GamePath GetPrimaryFile(GameInstallation installation) => GamePathProvier.PrimaryLauncherFile(installation.LocatorResult.Store);
}
