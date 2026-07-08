using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace NexusMods.Abstractions.Thunderstore.Models;

/// <summary>
/// Represents a remote package on Thunderstore (the all-versions container, analogous to a
/// Nexus Mods mod page). Thunderstore packages are global — they are not tied to a single
/// community/game, so no game reference is stored here.
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
    /// Back-reference to all known versions of this package.
    /// </summary>
    public static readonly BackReferenceAttribute<ThunderstoreVersionMetadata> Versions = new(ThunderstoreVersionMetadata.Package);
}
