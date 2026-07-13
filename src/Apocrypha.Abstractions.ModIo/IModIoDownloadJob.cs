using JetBrains.Annotations;
using Apocrypha.Abstractions.Downloads;
using Apocrypha.Abstractions.HttpDownloads;
using Apocrypha.Abstractions.ModIo.Models;

namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// A download job for an exact mod.io modfile.
/// </summary>
[PublicAPI]
public interface IModIoDownloadJob : IHttpDownloadJob, ILibraryDownloadJob
{
    /// <summary>
    /// Metadata of the modfile being downloaded.
    /// </summary>
    ModIoFileMetadata.ReadOnly FileMetadata { get; }
}
