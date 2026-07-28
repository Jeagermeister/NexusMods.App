using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Collections.Json;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;
using OneOf;

namespace Apocrypha.Games.CreationEngine.SortOrder;

/// <summary>
/// Seeds the Creation Engine plugin load order from a collection's curated `plugins` +
/// `pluginRules` data. Both the loadout-scoped sort order (what <see cref="PluginsFile"/> consumes
/// when writing plugins.txt) and the collection-scoped one are seeded, so the curated order takes
/// effect on the next Apply and survives a future switch to per-collection load orders.
/// </summary>
public class CollectionPluginOrderSeeder : ICollectionLoadOrderSeeder
{
    private readonly PluginSortOrderVariety _variety;
    private readonly ILogger<CollectionPluginOrderSeeder> _logger;

    public CollectionPluginOrderSeeder(PluginSortOrderVariety variety, ILogger<CollectionPluginOrderSeeder> logger)
    {
        _variety = variety;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<GameId> GameIds { get; } = [Fallout4.Fallout4.GameId, SkyrimSE.SkyrimSE.GameId];

    /// <inheritdoc />
    public async ValueTask SeedAsync(CollectionRoot root, LoadoutId loadoutId, CollectionGroupId collectionGroupId, CancellationToken token = default)
    {
        var curatedOrder = CuratedLoadOrder.Resolve(root.Plugins, root.PluginRules,
            warning => _logger.LogWarning("Curated load order: {Warning}", warning));

        if (curatedOrder.Count == 0)
        {
            _logger.LogDebug("Collection carries no curated plugin order; nothing to seed");
            return;
        }

        _logger.LogInformation("Seeding curated load order: {Count} plugins", curatedOrder.Count);

        await _variety.ApplyCuratedOrder(loadoutId, OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId), curatedOrder, token);
        await _variety.ApplyCuratedOrder(loadoutId, OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId), curatedOrder, token);
    }
}
