using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Abstractions.Thunderstore.Models;
using NexusMods.MnemonicDB.Abstractions;

namespace Apocrypha.Networking.Thunderstore;

/// <summary>
/// One-shot startup backfill for <see cref="ThunderstorePackageMetadata.Communities"/>:
/// packages downloaded before the attribute existed have no community listings, which
/// leaves them visible in every game's Library. Best-effort — a package that can't be
/// resolved (offline, delisted) stays unknown and is retried on the next launch.
/// </summary>
internal sealed class ThunderstoreCommunityBackfill : BackgroundService
{
    /// <summary>
    /// Keeps the backfill out of short-lived hosts (CLI verbs) and off the startup path.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IConnection _connection;
    private readonly IThunderstoreApiClient _apiClient;
    private readonly ILogger<ThunderstoreCommunityBackfill> _logger;

    public ThunderstoreCommunityBackfill(
        IConnection connection,
        IThunderstoreApiClient apiClient,
        ILogger<ThunderstoreCommunityBackfill> logger)
    {
        _connection = connection;
        _apiClient = apiClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            var packages = ThunderstorePackageMetadata.All(_connection.Db)
                .Where(package => !package.Communities.Any())
                .ToArray();
            if (packages.Length == 0) return;

            var backfilled = 0;
            using var tx = _connection.BeginTransaction();
            foreach (var package in packages)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var dto = await _apiClient.GetPackage(new PackageRef(package.PackageNamespace, package.Name), stoppingToken);
                if (dto is null || dto.CommunityListings.Length == 0)
                {
                    _logger.LogDebug("No community listings resolved for `{Package}`; will retry next launch", package.FullName);
                    continue;
                }

                foreach (var community in dto.CommunityListings.Select(listing => listing.Community).Distinct())
                    tx.Add(package.Id, ThunderstorePackageMetadata.Communities, community);
                backfilled++;
            }

            if (backfilled == 0) return;
            await tx.Commit();
            _logger.LogInformation("Backfilled Thunderstore community listings for {Count} package(s)", backfilled);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — nothing committed, retried next launch.
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Thunderstore community backfill failed; will retry next launch");
        }
    }
}
