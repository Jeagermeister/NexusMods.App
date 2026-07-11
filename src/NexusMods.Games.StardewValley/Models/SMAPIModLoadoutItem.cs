using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace NexusMods.Games.StardewValley.Models;

/// <remarks>
/// Legacy model: upstream planned to remove this "with a future migration".
/// The planned migration never landed and existing databases contain these entities,
/// so the model stays. Upstream's [Obsolete] marker was dropped — it only produced
/// CS0618 warnings in the MnemonicDB-generated code, not useful guidance.
/// </remarks>
[Include<LoadoutItemGroup>]
public partial class SMAPIModLoadoutItem : IModelDefinition
{
    private const string Namespace = "NexusMods.StardewValley.SMAPIModLoadoutItem";

    public static readonly ReferenceAttribute<SMAPIManifestLoadoutFile> Manifest = new(Namespace, nameof(Manifest));
}
