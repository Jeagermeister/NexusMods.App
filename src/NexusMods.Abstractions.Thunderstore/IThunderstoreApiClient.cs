using JetBrains.Annotations;
using NexusMods.Abstractions.Thunderstore.DTOs;

namespace NexusMods.Abstractions.Thunderstore;

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
}
