using JetBrains.Annotations;
using Apocrypha.Abstractions.Thunderstore.DTOs;

namespace Apocrypha.Abstractions.Thunderstore;

/// <summary>
/// Read-only client for the Thunderstore experimental API. All endpoints are anonymous.
/// </summary>
[PublicAPI]
public interface IThunderstoreApiClient
{
    /// <summary>
    /// Fetches a package (including its latest version) by namespace and name.
    /// Returns null if the package does not exist.
    /// </summary>
    Task<PackageDto?> GetPackage(PackageRef package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one exact version of a package. Returns null if the package or version does not exist.
    /// </summary>
    Task<PackageVersionDto?> GetVersion(PackageVersionRef version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a community's full v1 package index (every package with all versions and
    /// dependencies). One large request — the bulk alternative to hundreds of
    /// <see cref="GetPackage"/> calls when resolving modpack-sized dependency closures.
    /// </summary>
    IAsyncEnumerable<PackageIndexEntryDto> GetCommunityPackageIndex(string communitySlug, CancellationToken cancellationToken = default);
}
