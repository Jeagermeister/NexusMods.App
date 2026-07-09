using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Abstractions.Loadouts;

namespace NexusMods.Games.BepInEx.Models;

/// <summary>
/// Marks a loadout item group as an installed BepInEx loader pack.
/// </summary>
[PublicAPI]
[Include<LoadoutItemGroup>]
public partial class BepInExLoadoutItem : IModelDefinition
{
    // Historical value from the Phase 1 RoR2 pilot, kept verbatim: the attribute string is
    // the persisted identity of existing datoms, and MnemonicDB's query layer additionally
    // requires the final namespace segment to be unique app-wide — so no parallel
    // "NexusMods.BepInEx.*" attribute set can coexist with this one.
    private const string Namespace = "NexusMods.RiskOfRain2.BepInExLoadoutItem";

    /// <summary>
    /// The pack version, when known (e.g. from Thunderstore version metadata).
    /// </summary>
    public static readonly StringAttribute Version = new(Namespace, nameof(Version)) { IsOptional = true };
}
