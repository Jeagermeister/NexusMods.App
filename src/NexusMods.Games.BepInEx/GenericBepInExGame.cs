using System.Collections.Immutable;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Abstractions.Thunderstore;
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
public class GenericBepInExGame : IGame, IThunderstoreCommunityGame
{
    private const string GcdnAssetsBaseUrl = "https://gcdn.thunderstore.io/assets/";

    private readonly BepInExGameData _data;
    private readonly InstallRuleRouter _installRuleRouter;

    public BepInExGameData Data => _data;

    public string ThunderstoreCommunitySlug => _data.CommunitySlug;

    public GameId GameId => _data.GameId;
    public string DisplayName => _data.DisplayName;
    public Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => _data.NexusModsGameId;

    public StoreIdentifiers StoreIdentifiers { get; }

    /// <remarks>
    /// Runtime-fetched art (design §10): the community's 192×192 icon and the game's 360×480
    /// cover, disk-cached on first read; the shared placeholder serves when offline or when
    /// the schema carries no asset.
    /// </remarks>
    public IStreamFactory IconImage { get; }
    public IStreamFactory TileImage { get; }

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

        IconImage = CreateArtFactory(provider, data.CommunityIconUrl,
            new EmbeddedResourceStreamFactory<GenericBepInExGame>("NexusMods.Games.BepInEx.Resources.thumbnail.webp"));
        TileImage = CreateArtFactory(provider, data.CoverUrl,
            new EmbeddedResourceStreamFactory<GenericBepInExGame>("NexusMods.Games.BepInEx.Resources.tile.webp"));

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

        _installRuleRouter = new InstallRuleRouter(data.InstallRules);
        LibraryItemInstallers =
        [
            provider.GetRequiredService<BepInExPackInstaller>(),
            // Per-game instance: routing follows this game's schema installRules (design §6).
            new BepInExPluginInstaller(provider, _installRuleRouter, data.RelativeFileExclusions),
        ];
    }

    /// <summary>
    /// Collections install downloads that no installer claims into the schema's default route
    /// (Vortex parity, upstream #2553) instead of raising one advanced-installer dialog per mod.
    /// </summary>
    public Optional<GamePath> GetFallbackCollectionInstallDirectory(GameInstallation installation)
        => new GamePath(LocationId.Game, RelativePath.FromUnsanitizedInput(_installRuleRouter.DefaultRoute));

    /// <summary>
    /// Art is best-effort by design (§10): the placeholder serves when the schema carries no
    /// asset and in containers without HTTP or a filesystem (lean test hosts); the factory
    /// itself falls back per-read when the CDN is unreachable.
    /// </summary>
    private static IStreamFactory CreateArtFactory(IServiceProvider provider, string? gcdnPath, IStreamFactory placeholder)
    {
        if (gcdnPath is null) return placeholder;

        var httpClient = provider.GetService<HttpClient>();
        var fileSystem = provider.GetService<IFileSystem>();
        if (httpClient is null || fileSystem is null) return placeholder;

        return new CachedHttpStreamFactory(
            httpClient,
            new Uri(GcdnAssetsBaseUrl + gcdnPath),
            GameArtCache.GetCacheFile(fileSystem, gcdnPath),
            placeholder,
            provider.GetService<ILogger<GenericBepInExGame>>()
        );
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
