using JetBrains.Annotations;
using Apocrypha.Abstractions.Downloads;
using Apocrypha.Abstractions.HttpDownloads;
using Apocrypha.Abstractions.Thunderstore.Models;

namespace Apocrypha.Abstractions.Thunderstore;

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
