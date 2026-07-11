using DynamicData.Kernel;
using JetBrains.Annotations;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;

namespace Apocrypha.Abstractions.Downloads;

/// <summary>
/// A download job that lands in the Library and appears in the Downloads UI. Every mod
/// source's download job (Nexus Mods, Thunderstore, ...) implements this so
/// <see cref="IDownloadsService"/> can observe and control downloads source-agnostically.
/// </summary>
[PublicAPI]
public interface ILibraryDownloadJob : IDownloadJob
{
    /// <summary>
    /// Display name for the downloads UI (typically the mod name). May be empty; consumers
    /// fall back to the destination file name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// The game this download is for, or <c>default</c> when the source doesn't associate
    /// downloads with a game (the UI shows these as unknown).
    /// </summary>
    GameId GameId { get; }

    /// <summary>
    /// The page the download originated from.
    /// </summary>
    Uri DownloadPageUri { get; }

    /// <summary>
    /// Entity id of the source-specific metadata entity describing this download
    /// (e.g. Nexus file metadata, Thunderstore version metadata). Used for thumbnails.
    /// </summary>
    EntityId MetadataEntityId { get; }

    /// <summary>
    /// The job whose observables and pause/resume/cancel drive the downloads UI. Wrapper jobs
    /// (e.g. Nexus, which wraps an HTTP job) return the inner job here; jobs that ARE the HTTP
    /// transfer return null and the outer job is used directly.
    /// </summary>
    IJob? InnerJob { get; }

    /// <summary>
    /// Finds the library file this download produced. Only meaningful after completion;
    /// source-specific lookup.
    /// </summary>
    Optional<LibraryFile.ReadOnly> FindLibraryFile(IDb db);
}
