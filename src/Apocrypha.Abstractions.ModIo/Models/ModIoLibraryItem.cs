using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Abstractions.ModIo.Models;

/// <summary>
/// Represents a <see cref="LibraryItem"/> originating from mod.io.
/// </summary>
[PublicAPI]
[Include<LibraryItem>]
public partial class ModIoLibraryItem : IModelDefinition
{
    private const string Namespace = "Apocrypha.ModIo.ModIoLibraryItem";

    /// <summary>
    /// Remote metadata of the exact modfile this library item was downloaded from.
    /// </summary>
    public static readonly ReferenceAttribute<ModIoFileMetadata> File = new(Namespace, nameof(File));
}
