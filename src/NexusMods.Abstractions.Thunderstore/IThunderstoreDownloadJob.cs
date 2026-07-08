using JetBrains.Annotations;
using NexusMods.Abstractions.Downloads;
using NexusMods.Abstractions.HttpDownloads;
using NexusMods.Abstractions.Thunderstore.Models;

namespace NexusMods.Abstractions.Thunderstore;

/// <summary>
/// A download job for an exact Thunderstore package version.
/// </summary>
[PublicAPI]
public interface IThunderstoreDownloadJob : IHttpDownloadJob, ILibraryDownloadJob
{
    /// <summary>
    /// Metadata of the package version being downloaded.
    /// </summary>
    ThunderstoreVersionMetadata.ReadOnly VersionMetadata { get; }
}
