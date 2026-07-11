using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace Apocrypha.Games.MountAndBlade2Bannerlord.Models;

[Include<LoadoutItemGroup>]
public partial class BannerlordModuleLoadoutItem : IModelDefinition
{
    private const string Namespace = "NexusMods.MountAndBlade2Bannerlord.ModLoadoutItem";

    public static readonly ReferenceAttribute<ModuleInfoFileLoadoutFile> ModuleInfo = new(Namespace, nameof(ModuleInfo));
}
