using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;
using Apocrypha.Games.CreationEngine.SortOrder;
using NexusMods.Paths;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.CreationEngine;

public abstract class ACreationEngineSynchronizer : ALoadoutSynchronizer
{
    private Dictionary<GamePath, IIntrinsicFile> _intrinsicFiles;
    protected ACreationEngineSynchronizer(IServiceProvider provider, ICreationEngineGame game) : base(provider)
    {
        var pluginsFile = new PluginsFile(provider.GetRequiredService<ILogger<PluginsFile>>(), game, provider.GetRequiredService<PluginSortOrderVariety>());
        _intrinsicFiles = new Dictionary<GamePath, IIntrinsicFile>()
        {
            {pluginsFile.Path, pluginsFile},
        };

        // Games that declare a DLC manifest get it managed too — an unmanaged one can hold a stale
        // entry that crashes the main menu with no diagnostic anywhere in the app.
        if (game.DlcListFile is { } dlcListPath && game.KnownDlc.Count > 0)
        {
            var dlcListFile = new DlcListFile(dlcListPath, game.KnownDlc);
            _intrinsicFiles[dlcListFile.Path] = dlcListFile;
        }
    }

    
    private static readonly GamePath SavesPath = new GamePath(LocationId.Preferences, "Saves");
    protected override IGamePathFilter GamePathFilter { get; } = Apocrypha.Abstractions.Loadouts.Synchronizers.GamePathFilters.Create(path => path.InFolder(SavesPath));

    public override Dictionary<GamePath, IIntrinsicFile> IntrinsicFiles(Loadout.ReadOnly _) => _intrinsicFiles;
    
    public override bool IsIgnoredBackupPath(GamePath path)
    {
        // Don't backup BSA files by default
        return path.Extension == KnownCEExtensions.BSA || path.Extension == KnownCEExtensions.BA2;
    }
}
