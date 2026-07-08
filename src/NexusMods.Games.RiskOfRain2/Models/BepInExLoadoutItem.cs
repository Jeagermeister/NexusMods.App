using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Abstractions.Loadouts;

namespace NexusMods.Games.RiskOfRain2.Models;

/// <summary>
/// Marks a loadout item group as an installed BepInEx loader pack.
/// </summary>
[PublicAPI]
[Include<LoadoutItemGroup>]
public partial class BepInExLoadoutItem : IModelDefinition
{
    private const string Namespace = "NexusMods.RiskOfRain2.BepInExLoadoutItem";

    /// <summary>
    /// The pack version, when known (e.g. from Thunderstore version metadata).
    /// </summary>
    public static readonly StringAttribute Version = new(Namespace, nameof(Version)) { IsOptional = true };
}
