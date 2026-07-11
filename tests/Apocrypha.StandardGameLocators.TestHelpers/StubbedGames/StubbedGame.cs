using System.Collections.Immutable;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.IO;

// ReSharper disable InconsistentNaming

namespace Apocrypha.StandardGameLocators.TestHelpers.StubbedGames;

public class StubbedGame : IGame, IGameData<StubbedGame>
{
    public static GameId GameId { get; } = GameId.From("StubbedGame");
    public static string DisplayName => "Stubbed Game";

    // TODO: make None after moving to GameId
    public static Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => Sdk.NexusModsApi.NexusModsGameId.From(uint.MaxValue);

    public StoreIdentifiers StoreIdentifiers { get; } = new(GameId)
    {
        SteamAppIds = [42u],
        GOGProductIds = [42L],
        EADesktopSoftwareIds = ["ea-game-id"],
        EGSCatalogItemId = ["epic-game-id"],
        OriginManifestIds = ["origin-game-id"],
        XboxPackageIdentifiers = ["xbox-game-id"],
    };

    public IStreamFactory IconImage { get; } = new EmbeddedResourceStreamFactory<StubbedGame>("Apocrypha.StandardGameLocators.TestHelpers.Resources.question_mark_game.png");
    public IStreamFactory TileImage => throw new NotImplementedException("No game image for stubbed game.");

    private readonly IServiceProvider _serviceProvider;
    public StubbedGame(IServiceProvider provider)
    {
        _serviceProvider = provider;
    }

    public GamePath GetPrimaryFile(GameInstallation installation) => new(LocationId.Game, "");

    public ILoadoutSynchronizer Synchronizer => new DefaultSynchronizer(_serviceProvider);

    public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult)
    {
        return new Dictionary<LocationId, AbsolutePath>
        {
            { LocationId.Game, gameLocatorResult.Path.Combine("game")},
            { LocationId.Preferences, gameLocatorResult.Path.Combine("preferences")},
            { LocationId.Saves, gameLocatorResult.Path.Combine("saves")},
        }.ToImmutableDictionary();
    }

    public ILibraryItemInstaller[] LibraryItemInstallers =>
    [
        new StubbedGameInstaller(_serviceProvider),
    ];

    public IDiagnosticEmitter[] DiagnosticEmitters => [];
    public ISortOrderManager SortOrderManager => new SortOrderManager(_serviceProvider);

}
