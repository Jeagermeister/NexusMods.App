using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace Apocrypha.Abstractions.Thunderstore.Models;

/// <summary>
/// Represents a remote package on Thunderstore (the all-versions container, analogous to a
/// Nexus Mods mod page). Thunderstore packages are global — a package can be listed in any
/// number of communities (games), so game scoping is the <see cref="Communities"/> slug set
/// rather than a single game reference.
/// </summary>
[PublicAPI]
public partial class ThunderstorePackageMetadata : IModelDefinition
{
    private const string Namespace = "NexusMods.Thunderstore.ThunderstorePackageMetadata";

    /// <summary>
    /// The canonical <c>Namespace-Name</c> identifier — the natural lookup key.
    /// </summary>
    public static readonly StringAttribute FullName = new(Namespace, nameof(FullName)) { IsIndexed = true };

    /// <summary>
    /// The owning team's namespace, e.g. <c>bbepis</c>.
    /// </summary>
    public static readonly StringAttribute PackageNamespace = new(Namespace, nameof(PackageNamespace));

    /// <summary>
    /// The package name, e.g. <c>BepInExPack</c>.
    /// </summary>
    public static readonly StringAttribute Name = new(Namespace, nameof(Name));

    /// <summary>
    /// The package page on thunderstore.io.
    /// </summary>
    public static readonly UriAttribute PackageUri = new(Namespace, nameof(PackageUri));

    /// <summary>
    /// The package icon (256×256 PNG on the Thunderstore CDN).
    /// </summary>
    public static readonly UriAttribute IconUri = new(Namespace, nameof(IconUri)) { IsOptional = true };

    /// <summary>
    /// The community slugs (games) this package is listed under on thunderstore.io, from the
    /// package API's community listings. EMPTY MEANS UNKNOWN, not unlisted: rows created
    /// before this attribute existed are backfilled on startup, and consumers must keep
    /// unknown packages visible everywhere.
    /// </summary>
    public static readonly StringsAttribute Communities = new(Namespace, nameof(Communities));

    /// <summary>
    /// Back-reference to all known versions of this package.
    /// </summary>
    public static readonly BackReferenceAttribute<ThunderstoreVersionMetadata> Versions = new(ThunderstoreVersionMetadata.Package);
}
