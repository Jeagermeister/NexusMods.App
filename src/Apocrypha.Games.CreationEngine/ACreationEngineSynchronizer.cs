using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Sorting;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;
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
        var pluginsFile = new PluginsFile(provider.GetRequiredService<ILogger<PluginsFile>>(), game, provider.GetRequiredService<ISorter>());
        _intrinsicFiles = new Dictionary<GamePath, IIntrinsicFile>()
        {
            {pluginsFile.Path, pluginsFile},
        };
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
