using System.Runtime.CompilerServices;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Diagnostics.Values;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.BepInEx.Models;
using NexusMods.Sdk.Loadouts;

namespace NexusMods.Games.BepInEx.Emitters;

/// <summary>
/// Warns when the loadout contains BepInEx plugins but no BepInEx loader pack —
/// the game would launch entirely unmodded. Constructed once per family game with that
/// game's Thunderstore community, so the diagnostic links to the right place.
/// </summary>
public class MissingBepInExEmitter : ILoadoutDiagnosticEmitter
{
    private readonly NamedLink _communityLink;

    public MissingBepInExEmitter(string communitySlug)
    {
        _communityLink = new NamedLink("Thunderstore", new Uri($"https://thunderstore.io/c/{communitySlug}/"));
    }

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
            BepInExPackUri: _communityLink
        );
    }
}
