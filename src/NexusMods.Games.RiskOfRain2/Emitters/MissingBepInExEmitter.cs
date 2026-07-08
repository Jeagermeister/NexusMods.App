using System.Runtime.CompilerServices;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Diagnostics.Values;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RiskOfRain2.Models;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.RiskOfRain2.Emitters;

/// <summary>
/// Warns when the loadout contains BepInEx plugins but no BepInEx loader pack —
/// the game would launch entirely unmodded.
/// </summary>
public class MissingBepInExEmitter : ILoadoutDiagnosticEmitter
{
    private static readonly NamedLink BepInExPackLink = new("Thunderstore", new Uri("https://thunderstore.io/c/riskofrain2/p/bbepis/BepInExPack/"));

    public async IAsyncEnumerable<Diagnostic> Diagnose(
        Loadout.ReadOnly loadout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        var groups = LoadoutItem.FindByLoadout(loadout.Db, loadout).OfTypeLoadoutItemGroup().ToArray();
        var hasLoader = groups.OfTypeBepInExLoadoutItem().Any();
        var pluginCount = groups.OfTypeBepInExPluginLoadoutItem().Count();

        if (pluginCount == 0 || hasLoader) yield break;

        yield return Diagnostics.CreateMissingBepInEx(
            PluginCount: pluginCount,
            BepInExPackUri: BepInExPackLink
        );
    }
}
