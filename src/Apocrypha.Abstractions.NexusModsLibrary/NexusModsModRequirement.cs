using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using Apocrypha.Sdk.NexusModsApi;

namespace Apocrypha.Abstractions.NexusModsLibrary;

/// <summary>
/// Represents a single "required mod" relationship declared by a NexusMods mod page:
/// the mod identified by <see cref="OwnerUid"/> requires the mod identified by
/// <see cref="RequiredUid"/>.
/// </summary>
/// <remarks>
/// Ingested from the Nexus <c>modRequirements.nexusRequirements</c> API and used to
/// auto-enable an already-installed-but-disabled required mod when its dependant is
/// enabled. Kept as a standalone entity (rather than a back-reference on
/// <see cref="NexusModsModPageMetadata"/>) so the requirement graph can be queried by
/// <see cref="OwnerUid"/> without needing the owning mod page's entity id.
/// </remarks>
[PublicAPI]
public partial class NexusModsModRequirement : IModelDefinition
{
    private const string Namespace = "NexusMods.NexusModsLibrary.NexusModsModRequirement";

    /// <summary>
    /// The <see cref="ModUid"/> of the mod page that declares this requirement.
    /// </summary>
    public static readonly ModUidAttribute OwnerUid = new(Namespace, nameof(OwnerUid)) { IsIndexed = true };

    /// <summary>
    /// The <see cref="ModUid"/> of the mod that is required by <see cref="OwnerUid"/>.
    /// </summary>
    public static readonly ModUidAttribute RequiredUid = new(Namespace, nameof(RequiredUid)) { IsIndexed = true };

    /// <summary>
    /// Human-readable name of the required mod, as reported by Nexus. For diagnostics and UI only.
    /// </summary>
    public static readonly StringAttribute RequiredModName = new(Namespace, nameof(RequiredModName)) { IsOptional = true };
}
