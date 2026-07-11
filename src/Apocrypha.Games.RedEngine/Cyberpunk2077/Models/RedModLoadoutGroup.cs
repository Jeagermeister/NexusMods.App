using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Extensions;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Paths;

namespace Apocrypha.Games.RedEngine.Cyberpunk2077.Models;

/// <remarks>
/// Legacy model: upstream planned to identify RedMod groups by the presence of a
/// `{Game}/mods/&lt;redModName&gt;/info.json` child file instead of marking them explicitly.
/// The planned migration never landed and existing databases contain these entities,
/// so the model stays. Upstream's [Obsolete] marker was dropped — it only produced
/// CS0618 warnings in the MnemonicDB-generated code, not useful guidance.
/// </remarks>
[Include<LoadoutItemGroup>]
public partial class RedModLoadoutGroup : IModelDefinition
{
    private const string Namespace = "NexusMods.RedEngine.Cyberpunk2077.RedModLoadoutGroup";
    
    /// <summary>
    /// The info.json file for this RedMod
    /// </summary>
    public static readonly ReferenceAttribute<RedModInfoFile> RedModInfoFile = new(Namespace, nameof(RedModInfoFile));
}


