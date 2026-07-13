using JetBrains.Annotations;
using Apocrypha.Abstractions.ModIo.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// mod.io as a mod source: resolves mod links to metadata entities and creates download
/// jobs whose results land in the Library (the mod.io peer of the Thunderstore library facade).
/// </summary>
[PublicAPI]
public interface IModIoLibrary
{
    /// <summary>
    /// Resolves a game slug + mod slug (the parts of a <c>mod.io/g/{game}/m/{mod}</c> link)
    /// to the mod's metadata entity with its latest modfile, fetching and storing both on
    /// first sight.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The game or mod does not exist on mod.io, or the mod has no released file.</exception>
    Task<ModIoFileMetadata.ReadOnly> ResolveLatestFile(string gameNameId, string modNameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if a library item for this exact modfile already exists.
    /// </summary>
    bool IsAlreadyDownloaded(ulong fileId, IDb? db = null);

    /// <summary>
    /// Creates (and starts) a download job for an exact modfile. Pass the result to
    /// <c>ILibraryService.AddDownload</c> to land it in the Library.
    /// </summary>
    /// <remarks>
    /// The modfile's binary URL is fetched fresh at job creation (they expire), so this
    /// makes one API call per download.
    /// </remarks>
    Task<IJobTask<IModIoDownloadJob, AbsolutePath>> CreateDownloadJob(ModIoFileMetadata.ReadOnly fileMetadata, CancellationToken cancellationToken = default);
}
