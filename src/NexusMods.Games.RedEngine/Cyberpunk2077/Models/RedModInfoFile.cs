using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace NexusMods.Games.RedEngine.Cyberpunk2077.Models;

/// <remarks>
/// Legacy model: upstream planned to identify RedMod manifests by the path format
/// `{Game}/mods/&lt;redModName&gt;/info.json` instead of marking them explicitly.
/// The planned migration never landed and existing databases contain these entities,
/// so the model stays. Upstream's [Obsolete] marker was dropped — it only produced
/// CS0618 warnings in the MnemonicDB-generated code, not useful guidance.
/// </remarks>
[Include<LoadoutFile>]
public partial class RedModInfoFile : IModelDefinition
{
    private static string Namespace => "NexusMods.Games.RedEngine.Cyberpunk2077.RedModInfoFile";
    
    /// <summary>
    /// The internal name of the mod
    /// </summary>
    public static readonly StringAttribute Name = new(Namespace, nameof(Name));

    /// <summary>
    /// The internal version of the mod
    /// </summary>
    public static readonly StringAttribute Version = new(Namespace, nameof(Version));
}
