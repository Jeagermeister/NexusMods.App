using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Sdk.Library;

namespace NexusMods.Abstractions.Thunderstore.Models;

/// <summary>
/// Represents a <see cref="LibraryItem"/> originating from Thunderstore.
/// </summary>
[PublicAPI]
[Include<LibraryItem>]
public partial class ThunderstoreLibraryItem : IModelDefinition
{
    private const string Namespace = "NexusMods.Thunderstore.ThunderstoreLibraryItem";

    /// <summary>
    /// Remote metadata of the exact package version this library item was downloaded from.
    /// </summary>
    public static readonly ReferenceAttribute<ThunderstoreVersionMetadata> Version = new(Namespace, nameof(Version));
}
