using Apocrypha.Abstractions.Downloads;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Abstractions.Library.Jobs;

/// <summary>
/// A job that adds a download job to the library after the download has been completed
/// </summary>
public interface IAddDownloadJob : IJobDefinition<LibraryFile.ReadOnly>
{
    /// <summary>
    /// The download job
    /// </summary>
    public IJobTask<IDownloadJob, AbsolutePath> DownloadJob { get; init; }
}
