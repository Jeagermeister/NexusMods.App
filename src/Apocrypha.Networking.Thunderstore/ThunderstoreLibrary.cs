using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Abstractions.Thunderstore.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Networking.Thunderstore;

/// <summary>
/// Implementation of <see cref="IThunderstoreLibrary"/> — the Thunderstore peer of the
/// Nexus Mods library facade.
/// </summary>
public class ThunderstoreLibrary : IThunderstoreLibrary
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ThunderstoreLibrary> _logger;
    private readonly IConnection _connection;
    private readonly IThunderstoreApiClient _apiClient;

    /// <summary>
    /// Constructor.
    /// </summary>
    public ThunderstoreLibrary(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<ThunderstoreLibrary>>();
        _connection = serviceProvider.GetRequiredService<IConnection>();
        _apiClient = serviceProvider.GetRequiredService<IThunderstoreApiClient>();
    }

    /// <inheritdoc/>
    public async Task<ThunderstoreVersionMetadata.ReadOnly> GetOrAddVersion(
        PackageVersionRef version,
        CancellationToken cancellationToken = default)
    {
        var db = _connection.Db;
        if (TryFindVersion(db, version, out var existing)) return existing;

        var dto = await _apiClient.GetVersion(version, cancellationToken);
        if (dto is null) throw new KeyNotFoundException($"Package version `{version}` was not found on Thunderstore");

        using var tx = _connection.BeginTransaction();

        EntityId packageId;
        if (TryFindPackage(db, version.Package, out var existingPackage))
        {
            packageId = existingPackage.Id;
        }
        else
        {
            var newPackage = new ThunderstorePackageMetadata.New(tx)
            {
                FullName = version.Package.FullName,
                PackageNamespace = version.Package.Namespace,
                Name = version.Package.Name,
                PackageUri = ThunderstoreUrls.GetPackagePageUri(version.Package),
            };

            if (Uri.TryCreate(dto.Icon, UriKind.Absolute, out var iconUri))
                newPackage.IconUri = iconUri;

            packageId = newPackage.Id;
        }

        var newVersion = new ThunderstoreVersionMetadata.New(tx)
        {
            FullName = version.FullName,
            VersionNumber = version.Version,
            Dependencies = dto.Dependencies,
            UploadedAt = (dto.DateCreated ?? DateTimeOffset.UtcNow).UtcDateTime,
            PackageId = packageId,
        };

        var txResults = await tx.Commit();
        return txResults.Remap(newVersion);
    }

    /// <inheritdoc/>
    public async Task<PackageVersionRef> GetLatestVersion(PackageRef package, CancellationToken cancellationToken = default)
    {
        var dto = await _apiClient.GetPackage(package, cancellationToken);
        if (dto is null) throw new KeyNotFoundException($"Package `{package}` was not found on Thunderstore");
        return dto.Latest.VersionRef;
    }

    /// <inheritdoc/>
    public bool IsAlreadyDownloaded(PackageVersionRef version, IDb? db = null)
    {
        db ??= _connection.Db;
        if (!TryFindVersion(db, version, out var metadata)) return false;
        return metadata.LibraryItems.Any();
    }

    /// <inheritdoc/>
    public async Task<IJobTask<IThunderstoreDownloadJob, AbsolutePath>> CreateDownloadJob(
        PackageVersionRef version,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetOrAddVersion(version, cancellationToken);
        _logger.LogInformation("Starting Thunderstore download of `{Version}`", version.FullName);
        return ThunderstoreDownloadJob.Create(_serviceProvider, metadata);
    }

    private static bool TryFindVersion(IDb db, PackageVersionRef version, out ThunderstoreVersionMetadata.ReadOnly result)
    {
        foreach (var entity in ThunderstoreVersionMetadata.FindByFullName(db, version.FullName))
        {
            result = entity;
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryFindPackage(IDb db, PackageRef package, out ThunderstorePackageMetadata.ReadOnly result)
    {
        foreach (var entity in ThunderstorePackageMetadata.FindByFullName(db, package.FullName))
        {
            result = entity;
            return true;
        }

        result = default;
        return false;
    }
}
