using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace Apocrypha.Abstractions.ModIo.Models;

/// <summary>
/// Represents one published file (modfile) of a mod.io mod (analogous to a Nexus Mods file).
/// </summary>
[PublicAPI]
public partial class ModIoFileMetadata : IModelDefinition
{
    private const string Namespace = "Apocrypha.ModIo.ModIoFileMetadata";

    /// <summary>
    /// The mod.io numeric modfile id — the natural lookup key.
    /// </summary>
    public static readonly UInt64Attribute FileId = new(Namespace, nameof(FileId)) { IsIndexed = true };

    /// <summary>
    /// The file's version label. Free-form on mod.io and not always present.
    /// </summary>
    public static readonly StringAttribute Version = new(Namespace, nameof(Version)) { IsOptional = true };

    /// <summary>
    /// The uploaded archive's file name.
    /// </summary>
    public static readonly StringAttribute FileName = new(Namespace, nameof(FileName));

    /// <summary>
    /// The archive size in bytes.
    /// </summary>
    public static readonly SizeAttribute Size = new(Namespace, nameof(Size)) { IsOptional = true };

    /// <summary>
    /// When this file was uploaded.
    /// </summary>
    public static readonly TimestampAttribute UploadedAt = new(Namespace, nameof(UploadedAt)) { IsOptional = true };

    /// <summary>
    /// Reference to the mod this file belongs to.
    /// </summary>
    public static readonly ReferenceAttribute<ModIoModMetadata> Mod = new(Namespace, nameof(Mod));

    /// <summary>
    /// Library items that link to this file.
    /// </summary>
    public static readonly BackReferenceAttribute<ModIoLibraryItem> LibraryItems = new(ModIoLibraryItem.File);
}
