using JetBrains.Annotations;
using Apocrypha.Abstractions.Downloads;

namespace Apocrypha.Abstractions.NexusModsLibrary;

/// <summary>
/// Represents an NXM download.
/// </summary>
[PublicAPI]
public interface INXMDownloadJob : IDownloadJob
{
    NexusModsFileMetadata.ReadOnly FileMetadata { get; }
}
