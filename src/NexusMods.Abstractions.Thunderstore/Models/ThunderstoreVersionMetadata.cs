using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace NexusMods.Abstractions.Thunderstore.Models;

/// <summary>
/// Represents one published version of a Thunderstore package (analogous to a Nexus Mods file).
/// </summary>
[PublicAPI]
public partial class ThunderstoreVersionMetadata : IModelDefinition
{
    private const string Namespace = "NexusMods.Thunderstore.ThunderstoreVersionMetadata";

    /// <summary>
    /// The canonical <c>Namespace-Name-1.2.3</c> identifier (a Thunderstore dependency string) —
    /// the natural lookup key.
    /// </summary>
    public static readonly StringAttribute FullName = new(Namespace, nameof(FullName)) { IsIndexed = true };

    /// <summary>
    /// The strict <c>major.minor.patch</c> version number, e.g. <c>5.4.2100</c>.
    /// </summary>
    public static readonly StringAttribute VersionNumber = new(Namespace, nameof(VersionNumber));

    /// <summary>
    /// The exact-version dependency strings (<c>Namespace-Name-1.2.3</c>) published for this
    /// version, stored verbatim — they are immutable facts about a published version.
    /// </summary>
    public static readonly StringsAttribute Dependencies = new(Namespace, nameof(Dependencies));

    /// <summary>
    /// When this version was published.
    /// </summary>
    public static readonly TimestampAttribute UploadedAt = new(Namespace, nameof(UploadedAt));

    /// <summary>
    /// Reference to the package this version belongs to.
    /// </summary>
    public static readonly ReferenceAttribute<ThunderstorePackageMetadata> Package = new(Namespace, nameof(Package));

    /// <summary>
    /// Library items that link to this version.
    /// </summary>
    public static readonly BackReferenceAttribute<ThunderstoreLibraryItem> LibraryItems = new(ThunderstoreLibraryItem.Version);
}
