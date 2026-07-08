using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Abstractions.Loadouts;

namespace NexusMods.Games.RiskOfRain2.Models;

/// <summary>
/// Marks a loadout item group as an installed BepInEx plugin package.
/// </summary>
[PublicAPI]
[Include<LoadoutItemGroup>]
public partial class BepInExPluginLoadoutItem : IModelDefinition
{
    private const string Namespace = "NexusMods.RiskOfRain2.BepInExPluginLoadoutItem";

    /// <summary>
    /// Marker attribute.
    /// </summary>
    public static readonly MarkerAttribute Plugin = new(Namespace, nameof(Plugin));
}
