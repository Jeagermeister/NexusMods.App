using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using Apocrypha.Abstractions.Loadouts;

namespace Apocrypha.Games.BepInEx.Models;

/// <summary>
/// Marks a loadout item group as an installed BepInEx plugin package.
/// </summary>
[PublicAPI]
[Include<LoadoutItemGroup>]
public partial class BepInExPluginLoadoutItem : IModelDefinition
{
    // Historical value from the Phase 1 RoR2 pilot, kept verbatim — see BepInExLoadoutItem.
    private const string Namespace = "NexusMods.RiskOfRain2.BepInExPluginLoadoutItem";

    /// <summary>
    /// Marker attribute.
    /// </summary>
    public static readonly MarkerAttribute Plugin = new(Namespace, nameof(Plugin));
}
