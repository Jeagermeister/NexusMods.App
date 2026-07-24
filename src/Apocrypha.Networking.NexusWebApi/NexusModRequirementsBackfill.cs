using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Networking.NexusWebApi.Extensions;
using NexusMods.MnemonicDB.Abstractions;

namespace Apocrypha.Networking.NexusWebApi;

/// <summary>
/// One-shot startup backfill for <see cref="NexusModsModPageMetadata.RequirementsCheckedAt"/>:
/// mod pages downloaded before the dependency feature existed have never had their Nexus
/// "required mods" fetched, so enabling such a mod can't auto-enable its dependencies. This
/// re-queries each unchecked mod page once to persist its <see cref="NexusModsModRequirement"/>
/// edges. Best-effort — a page that can't be resolved (offline, delisted) stays unchecked and
/// is retried on the next launch.
/// </summary>
internal sealed class NexusModRequirementsBackfill : BackgroundService
{
    /// <summary>
    /// Keeps the backfill out of short-lived hosts (CLI verbs) and off the startup path.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gentle spacing between Nexus API calls so a large library doesn't burst the rate limit.
    /// </summary>
    private static readonly TimeSpan BetweenRequests = TimeSpan.FromMilliseconds(250);

    private readonly IConnection _connection;
    private readonly IGraphQlClient _graphQlClient;
    private readonly ILogger<NexusModRequirementsBackfill> _logger;

    public NexusModRequirementsBackfill(
        IConnection connection,
        IGraphQlClient graphQlClient,
        ILogger<NexusModRequirementsBackfill> logger)
    {
        _connection = connection;
        _graphQlClient = graphQlClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            var pending = NexusModsModPageMetadata.All(_connection.Db)
                .Where(page => !NexusModsModPageMetadata.RequirementsCheckedAt.IsIn(page))
                .Select(page => page.Uid)
                .ToArray();
            if (pending.Length == 0) return;

            _logger.LogInformation("Backfilling Nexus mod requirements for {Count} mod page(s)", pending.Length);

            var backfilled = 0;
            foreach (var uid in pending)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var result = await _graphQlClient.QueryMod(uid.ModId, uid.GameId, stoppingToken);
                if (!result.TryGetData(out var mod))
                {
                    _logger.LogDebug("Could not resolve mod page {ModId} for requirements; will retry next launch", uid.ModId);
                    continue;
                }

                // Commit per page so partial progress survives an early shutdown and each page is
                // marked checked as soon as it succeeds.
                using var tx = _connection.BeginTransaction();
                mod.Resolve(_connection.Db, tx);
                await tx.Commit();
                backfilled++;

                await Task.Delay(BetweenRequests, stoppingToken);
            }

            _logger.LogInformation("Backfilled Nexus mod requirements for {Count} mod page(s)", backfilled);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — already-committed pages persist, the rest retry next launch.
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Nexus mod requirements backfill failed; will retry next launch");
        }
    }
}
