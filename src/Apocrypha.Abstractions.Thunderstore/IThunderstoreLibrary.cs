using JetBrains.Annotations;
using Apocrypha.Abstractions.Thunderstore.DTOs;
using Apocrypha.Abstractions.Thunderstore.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Abstractions.Thunderstore;

/// <summary>
/// Thunderstore as a mod source: resolves package versions to metadata entities and creates
/// download jobs whose results land in the Library (the Thunderstore peer of the Nexus Mods
/// library facade).
/// </summary>
[PublicAPI]
public interface IThunderstoreLibrary
{
    /// <summary>
    /// Returns the metadata entity for an exact package version, fetching and storing it
    /// (and its package) on first sight.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The package or version does not exist on Thunderstore.</exception>
    Task<ThunderstoreVersionMetadata.ReadOnly> GetOrAddVersion(PackageVersionRef version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="GetOrAddVersion(PackageVersionRef,CancellationToken)"/> but from an
    /// already-fetched version DTO (e.g. out of a community package index), avoiding
    /// per-package API round-trips. <paramref name="knownCommunities"/> game-scopes the
    /// package in the Library when the caller already knows the community; pass null to
    /// look listings up via the API instead.
    /// </summary>
    Task<ThunderstoreVersionMetadata.ReadOnly> GetOrAddVersion(PackageVersionDto dto, IReadOnlyCollection<string>? knownCommunities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the latest published version of a package.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The package does not exist on Thunderstore.</exception>
    Task<PackageVersionRef> GetLatestVersion(PackageRef package, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if a library item for this exact package version already exists.
    /// </summary>
    bool IsAlreadyDownloaded(PackageVersionRef version, IDb? db = null);

    /// <summary>
    /// Creates (and starts) a download job for an exact package version. Pass the result to
    /// <c>ILibraryService.AddDownload</c> to land it in the Library.
    /// </summary>
    Task<IJobTask<IThunderstoreDownloadJob, AbsolutePath>> CreateDownloadJob(PackageVersionRef version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="CreateDownloadJob(PackageVersionRef,CancellationToken)"/> but from an
    /// already-fetched version DTO, avoiding per-package API round-trips (see
    /// <see cref="GetOrAddVersion(PackageVersionDto,IReadOnlyCollection{string},CancellationToken)"/>).
    /// </summary>
    Task<IJobTask<IThunderstoreDownloadJob, AbsolutePath>> CreateDownloadJob(PackageVersionDto dto, IReadOnlyCollection<string>? knownCommunities, CancellationToken cancellationToken = default);
}
