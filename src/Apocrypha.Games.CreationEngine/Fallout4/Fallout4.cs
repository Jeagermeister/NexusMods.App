using System.Collections.Immutable;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Records;
using Apocrypha.Abstractions.Diagnostics.Emitters;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Library.Installers;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;
using Apocrypha.Games.CreationEngine.Emitters;
using Apocrypha.Games.CreationEngine.Installers;
using Apocrypha.Games.FOMOD;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.IO;

namespace Apocrypha.Games.CreationEngine.Fallout4;

public class Fallout4 : ICreationEngineGame, IGameData<Fallout4>
{
    private readonly IStreamSourceDispatcher _streamSource;

    public static GameId GameId { get; } = GameId.From("CreationEngine.Fallout4");
    public static string DisplayName => "Fallout 4";
    public static Optional<Sdk.NexusModsApi.NexusModsGameId> NexusModsGameId => Sdk.NexusModsApi.NexusModsGameId.From(1151);

    public StoreIdentifiers StoreIdentifiers { get; } = new(GameId)
    {
        SteamAppIds = [377160u],
        GOGProductIds = [1998527297L],
    };

    public IStreamFactory IconImage { get; } = new EmbeddedResourceStreamFactory<Fallout4>("Apocrypha.Games.CreationEngine.Resources.Fallout4.thumbnail.webp");
    public IStreamFactory TileImage { get; } = new EmbeddedResourceStreamFactory<Fallout4>("Apocrypha.Games.CreationEngine.Resources.Fallout4.tile.webp");

    private readonly Lazy<ILoadoutSynchronizer> _synchronizer;
    public ILoadoutSynchronizer Synchronizer => _synchronizer.Value;
    public ILibraryItemInstaller[] LibraryItemInstallers { get; }
    private readonly Lazy<ISortOrderManager> _sortOrderManager;
    public ISortOrderManager SortOrderManager => _sortOrderManager.Value;
    public IDiagnosticEmitter[] DiagnosticEmitters { get; }

    public Fallout4(IServiceProvider provider)
    {
        _streamSource = provider.GetRequiredService<IStreamSourceDispatcher>();

        _synchronizer = new Lazy<ILoadoutSynchronizer>(() => new Fallout4Synchronizer(provider, this));
        _sortOrderManager = new Lazy<ISortOrderManager>(() =>
        {
            var sortOrderManager = provider.GetRequiredService<SortOrderManager>();
            sortOrderManager.RegisterSortOrderVarieties(
                sortOrderVarieties: [
                    provider.GetRequiredService<SortOrder.PluginSortOrderVariety>(),
                ],
                game: this
            );
            return sortOrderManager;
        });

        DiagnosticEmitters =
        [
            new MissingMasterEmitter(this),
            new SaveBreakingChangeEmitter(),
            new EngineLimitsEmitter(this, _streamSource),
        ];

        LibraryItemInstallers = 
        [
            FomodXmlInstaller.Create(provider, new GamePath(LocationId.Game, "Data")),
            new StopPatternInstaller(provider)
            {
                GameId = GameId,
                GameAliases = ["Fallout 4", "Fallout4", "FO4", "F4"],
                TopLevelDirs = KnownPaths.CommonTopLevelFolders,
                StopPatterns = ["(^|/)f4se(/|$)"],
                EngineFiles = [
                    // F4SE
                    @"f4se_loader\.exe", 
                    @"f4se_.*\.dll",
                    // Plugin Preloader (new
                    @"winhttp\.dll",
                    @"xSE\ PluginPreloader\.xml",
                    // Plugin Preloader (old)
                    @"IpHlpAPI\.dll",
                ],
            }.Build(),
        ];
    }

    public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult)
    {
        return new Dictionary<LocationId, AbsolutePath>()
        {
            { LocationId.Game, gameLocatorResult.Path },
            { LocationId.AppData, fileSystem.GetKnownPath(KnownPath.LocalApplicationDataDirectory) / "Fallout4" },
            { LocationId.Preferences, KnownPaths.MyGamesOrFallback(fileSystem) / "Fallout4" },
        }.ToImmutableDictionary();
    }

    public GamePath GetPrimaryFile(GameInstallation installation) => new(LocationId.Game, "Fallout4.exe");

    /// <summary>
    /// Collection content with no better destination belongs in the game's mod folder, and
    /// replicated collection files are declared relative to it -- without this they land in the
    /// game root where the engine never loads them.
    /// </summary>
    public Optional<GamePath> GetFallbackCollectionInstallDirectory(GameInstallation installation) => new GamePath(LocationId.Game, "Data");

    private static readonly GroupMask EmptyGroupMask = new(false);
    public async ValueTask<IMod?> ParsePlugin(Hash hash, RelativePath? name = null)
    {
        var fileName = name?.FileName.ToString() ?? "unknown.esm";
        var key = ModKey.FromFileName(fileName);
        await using var stream = await _streamSource.OpenAsync(hash);
        // An unknown hash yields no stream (e.g. a plugin whose archive was GC'd) -- callers
        // treat a null header as "skip this plugin", which must not take down the whole pass.
        if (stream == null) return null;
        var meta = ParsingMeta.Factory(BinaryReadParameters.Default, GameRelease.Fallout4, key, stream);
        await using var mutagenStream = new MutagenBinaryReadStream(stream, meta);
        using var frame = new MutagenFrame(mutagenStream);
        return Fallout4Mod.CreateFromBinary(frame, Fallout4Release.Fallout4, EmptyGroupMask);
    }

    public GamePath PluginsFile => Fallout4KnownPaths.PluginsFile;

    public GamePath? DlcListFile => Fallout4KnownPaths.DlcListFile;

    public IReadOnlyList<RelativePath> KnownDlc => Fallout4KnownPaths.Dlc;

    /// <summary>
    /// Fallout 4's general-archive ceiling. Confirmed the hard way: 334 enabled-plugin archives
    /// on a 908-mod collection produced main-menu access violations that bisected to file-handle
    /// exhaustion, not to any individual mod.
    /// </summary>
    public int? MaxGeneralArchives => 256;
}
