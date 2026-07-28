using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.CreationEngine.Models;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using Apocrypha.Sdk.Loadouts;
using OneOf;
using SortOrderModel = Apocrypha.Abstractions.Loadouts.SortOrder;

namespace Apocrypha.Games.CreationEngine.SortOrder;

/// <summary>
/// The plugin load order for Creation Engine games (`plugins.txt`). Greater index wins: a plugin
/// loaded later overrides the records of plugins loaded before it.
/// </summary>
/// <remarks>
/// The persisted order is a preference, not a promise: <see cref="PluginsFile"/> re-applies master
/// references as hard constraints when the file is written, so a curated or hand-arranged order
/// can never deploy a plugin ahead of its masters.
/// </remarks>
public class PluginSortOrderVariety : ASortOrderVariety<
    SortItemKey<string>,
    PluginReactiveSortItem,
    PluginSortItemLoadoutData,
    PluginSortItemData>
{
    private static readonly SortOrderVarietyId StaticVarietyId = SortOrderVarietyId.From(new Guid("5D9B5FD8-4C21-4A4B-9E9C-0A6C36E1BB6B"));

    private readonly ILogger<PluginSortOrderVariety> _logger;

    public override SortOrderVarietyId SortOrderVarietyId => StaticVarietyId;

    public PluginSortOrderVariety(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILogger<PluginSortOrderVariety>>();
    }

    public override SortOrderUiMetadata SortOrderUiMetadata { get; } = new()
    {
        SortOrderName = "Plugin Load Order",
        OverrideInfoTitle = "Load Order for Creation Engine plugins - Last Loaded Wins",
        OverrideInfoMessage = """
                              Creation Engine games load plugins (.esm, .esl, .esp) in the order listed in plugins.txt. If two plugins modify the same game record, the one loaded later wins and overrides the changes of those loaded earlier.
                              Master references always take precedence: a plugin can never load before a plugin it requires, regardless of its position here.
                              """,
        WinnerIndexToolTip = "Last Loaded Plugin Wins: plugins that load later override record changes from plugins loaded before them.",
        IndexColumnHeader = "Load Order",
        DisplayNameColumnHeader = "Plugin Name",
        EmptyStateMessageTitle = "No plugins detected",
        EmptyStateMessageContents = "Mods that ship plugin files (.esm, .esl, .esp) will appear here for load order configuration once installed.",
        LearnMoreUrl = string.Empty,
    };

    public override async ValueTask<SortOrderId> GetOrCreateSortOrderFor(
        LoadoutId loadoutId,
        OneOf<LoadoutId, CollectionGroupId> parentEntity,
        CancellationToken token = default)
    {
        var optionalSortOrderId = GetSortOrderIdFor(parentEntity);
        if (optionalSortOrderId.HasValue)
            return optionalSortOrderId.Value;

        token.ThrowIfCancellationRequested();

        using var ts = Connection.BeginTransaction();
        var newSortOrder = new SortOrderModel.New(ts)
        {
            LoadoutId = loadoutId,
            ParentEntity = parentEntity,
            SortOrderTypeId = SortOrderVarietyId.Value,
        };

        var commitResult = await ts.Commit();
        return commitResult.Remap(newSortOrder).SortOrderId;
    }

    public override IObservable<IChangeSet<PluginReactiveSortItem, SortItemKey<string>>> GetSortOrderItemsChangeSet(SortOrderId sortOrderId)
    {
        var sortOrder = SortOrderModel.Load(Connection.Db, sortOrderId);
        if (!sortOrder.IsValid())
            return Observable.Empty<IChangeSet<PluginReactiveSortItem, SortItemKey<string>>>();

        var loadoutId = sortOrder.LoadoutId;

        return PluginSortOrderExtensions.ObservePluginSortOrder(Connection, sortOrderId, loadoutId)
            .Transform(row =>
                {
                    var model = new PluginReactiveSortItem(
                        row.SortIndex,
                        row.PluginName,
                        modName: row.ModName ?? row.PluginName,
                        isActive: row.IsEnabled ?? false
                    );

                    if (row.ModGroupId == null) return model;

                    model.ModGroupId = LoadoutItemGroupId.From(row.ModGroupId.Value);
                    model.LoadoutData = new PluginSortItemLoadoutData(
                        row.PluginName,
                        model.IsActive,
                        model.ModName,
                        model.ModGroupId
                    );

                    return model;
                }
            );
    }

    public override IReadOnlyList<PluginReactiveSortItem> GetSortOrderItems(SortOrderId sortOrderId, IDb? db)
    {
        var dbToUse = db ?? Connection.Db;
        var sortOrder = SortOrderModel.Load(dbToUse, sortOrderId);
        if (!sortOrder.IsValid())
            return [];

        var optionalCollection = sortOrder.ParentEntity.Match(
            _ => Optional<CollectionGroupId>.None,
            collectionGroupId => Optional<CollectionGroupId>.Create(collectionGroupId)
        );

        var sortingData = RetrieveSortOrder(sortOrderId, dbToUse);
        var loadoutData = RetrieveLoadoutData(sortOrder.LoadoutId, optionalCollection, dbToUse);

        var reconciled = Reconcile(sortingData, loadoutData);

        return reconciled.Select(tuple => new PluginReactiveSortItem(
                tuple.SortedEntry.SortIndex,
                tuple.ItemLoadoutData.FileName,
                tuple.ItemLoadoutData.ModName,
                tuple.ItemLoadoutData.IsEnabled
            )
            {
                ModGroupId = tuple.ItemLoadoutData.ModGroupId,
                LoadoutData = tuple.ItemLoadoutData,
            }
        ).ToList();
    }

    /// <summary>
    /// Returns the persisted plugin order for the loadout as file names in load order, in display
    /// casing. Empty when no sort order exists in this db revision.
    /// </summary>
    public IReadOnlyList<string> GetPersistedPluginOrder(LoadoutId loadoutId, IDb? db = null)
    {
        var dbToUse = db ?? Connection.Db;
        var sortOrderId = GetSortOrderIdFor(loadoutId, dbToUse);
        if (!sortOrderId.HasValue) return [];

        return RetrieveSortOrder(sortOrderId.Value, dbToUse)
            .Select(item => item.PluginName)
            .ToList();
    }

    /// <summary>
    /// Rewrites the sort order of <paramref name="parentEntity"/> to follow a curated sequence:
    /// curated plugins first, in curated order; every other plugin in the loadout keeps its
    /// current relative order after them; plugins tracked by neither are appended in conventional
    /// (class, name) order.
    /// </summary>
    public async ValueTask ApplyCuratedOrder(
        LoadoutId loadoutId,
        OneOf<LoadoutId, CollectionGroupId> parentEntity,
        IReadOnlyList<string> curatedOrder,
        CancellationToken token = default)
    {
        if (curatedOrder.Count == 0) return;

        var sortOrderId = await GetOrCreateSortOrderFor(loadoutId, parentEntity, token);

        var optionalCollection = parentEntity.Match(
            _ => Optional<CollectionGroupId>.None,
            collectionGroupId => Optional<CollectionGroupId>.Create(collectionGroupId)
        );

        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();

            var db = Connection.Db;
            var currentOrder = RetrieveSortOrder(sortOrderId, db);
            var loadoutData = RetrieveLoadoutData(loadoutId, optionalCollection, db);
            var loadoutKeys = loadoutData.Select(item => item.Key).ToHashSet();

            var newOrder = new List<PluginSortItemData>(loadoutData.Count);
            var placed = new HashSet<SortItemKey<string>>();

            // The curated sequence leads. Curated plugins missing from the loadout are skipped
            // rather than persisted: reconciliation would prune them again on the next change.
            foreach (var pluginName in curatedOrder)
            {
                var key = PluginReactiveSortItem.MakeKey(pluginName);
                if (!loadoutKeys.Contains(key) || !placed.Add(key)) continue;
                newOrder.Add(new PluginSortItemData(pluginName, newOrder.Count));
            }
            var curatedCount = newOrder.Count;

            // Plugins the curated data does not mention keep their existing relative order...
            foreach (var existing in currentOrder)
            {
                if (!loadoutKeys.Contains(existing.Key) || !placed.Add(existing.Key)) continue;
                newOrder.Add(new PluginSortItemData(existing.PluginName, newOrder.Count));
            }

            // ...and plugins tracked by neither are appended in conventional order.
            var newcomers = loadoutData
                .Where(item => !placed.Contains(item.Key))
                .OrderBy(item => LoadOrderSorting.LoadOrderClassOf(item.FileName))
                .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase);
            foreach (var item in newcomers)
            {
                if (!placed.Add(item.Key)) continue;
                newOrder.Add(new PluginSortItemData(item.FileName, newOrder.Count));
            }

            if (await TryPersistSortOrder(sortOrderId, newOrder, db, token))
            {
                _logger.LogInformation(
                    "Applied curated load order to sort order {SortOrderId}: {CuratedCount} curated of {TotalCount} plugins",
                    sortOrderId, curatedCount, newOrder.Count);
                return;
            }

            _logger.LogWarning("Applying curated load order to {SortOrderId} hit a concurrent change, retrying ({Attempt}/3)", sortOrderId, attempt + 1);
        }

        _logger.LogError("Failed to apply curated load order to sort order {SortOrderId} after 3 attempts", sortOrderId);
    }

    protected override void PersistSortOrderCore(
        SortOrderId sortOrderId,
        IReadOnlyList<PluginSortItemData> newOrder,
        ITransaction tx,
        IDb startingDb,
        CancellationToken token = default)
    {
        var persistentSortOrderEntries = startingDb.RetrievePluginSortableEntries(sortOrderId);

        token.ThrowIfCancellationRequested();

        var newIndexByKey = new Dictionary<SortItemKey<string>, int>(newOrder.Count);
        for (var i = 0; i < newOrder.Count; i++)
        {
            newIndexByKey.TryAdd(newOrder[i].Key, i);
        }

        // Remove outdated persistent items, update the index of the ones that remain
        var persistedKeys = new HashSet<SortItemKey<string>>(persistentSortOrderEntries.Length);
        foreach (var dbItem in persistentSortOrderEntries)
        {
            var key = PluginReactiveSortItem.MakeKey(dbItem.PluginName);
            if (!newIndexByKey.TryGetValue(key, out var liveIdx) || !persistedKeys.Add(key))
            {
                tx.Delete(dbItem, recursive: false);
                continue;
            }

            if (dbItem.AsSortOrderItem().SortIndex != liveIdx)
            {
                tx.Add(dbItem, SortOrderItem.SortIndex, liveIdx);
            }
        }

        // Add new items
        for (var i = 0; i < newOrder.Count; i++)
        {
            var newItem = newOrder[i];
            if (persistedKeys.Contains(newItem.Key))
                continue;

            var newDbItem = new SortOrderItem.New(tx)
            {
                ParentSortOrderId = sortOrderId,
                SortIndex = i,
            };

            _ = new PluginSortOrderItem.New(tx, newDbItem)
            {
                SortOrderItem = newDbItem,
                PluginName = newItem.PluginName,
            };
        }

        token.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    protected override IReadOnlyList<PluginSortItemData> RetrieveSortOrder(SortOrderId sortOrderEntityId, IDb dbToUse)
    {
        return PluginSortOrderExtensions.RetrievePluginSortOrderItems(dbToUse, sortOrderEntityId);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<PluginSortItemLoadoutData> RetrieveLoadoutData(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId, IDb? db)
    {
        var dbToUse = db ?? Connection.Db;

        // Like the REDmod variety, this looks at the whole loadout even for collection-scoped sort
        // orders: the game loads one plugins.txt, and per-collection scoping of the item list is a
        // UI concern that does not exist yet.
        return PluginSortOrderExtensions.RetrieveWinningPluginsInLoadout(dbToUse, loadoutId)
            .Select(row => new PluginSortItemLoadoutData(
                row.PluginFileName,
                row.IsEnabled,
                row.ModName,
                row.ModGroupId == 0 ? Optional<LoadoutItemGroupId>.None : LoadoutItemGroupId.From(row.ModGroupId)
            ))
            .ToList();
    }

    /// <inheritdoc />
    protected override IReadOnlyList<(PluginSortItemData SortedEntry, PluginSortItemLoadoutData ItemLoadoutData)> Reconcile(
        IReadOnlyList<PluginSortItemData> sourceSortedEntries,
        IReadOnlyList<PluginSortItemLoadoutData> loadoutDataItems)
    {
        var loadoutItemsDict = new Dictionary<SortItemKey<string>, PluginSortItemLoadoutData>(loadoutDataItems.Count);
        foreach (var item in loadoutDataItems)
        {
            loadoutItemsDict.TryAdd(item.Key, item);
        }

        var results = new List<(PluginSortItemData SortedEntry, PluginSortItemLoadoutData ItemLoadoutData)>(loadoutItemsDict.Count);
        var processedKeys = new HashSet<SortItemKey<string>>(sourceSortedEntries.Count);

        // Existing entries keep their order; entries with no matching plugin in the loadout drop out.
        foreach (var sortedEntry in sourceSortedEntries)
        {
            if (!loadoutItemsDict.TryGetValue(sortedEntry.Key, out var loadoutItemData))
                continue;
            if (!processedKeys.Add(sortedEntry.Key))
                continue;

            results.Add((sortedEntry, loadoutItemData));
        }

        // Plugins new to the sort order are appended at the end in conventional (class, name)
        // order: for Creation Engine plugins the greater index wins, so new arrivals load last.
        var itemsToAdd = loadoutItemsDict.Values
            .Where(item => !processedKeys.Contains(item.Key))
            .OrderBy(item => LoadOrderSorting.LoadOrderClassOf(item.FileName))
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(loadoutItemData => (
                new PluginSortItemData(loadoutItemData.FileName, 0), // SortIndex is rewritten below
                loadoutItemData
            ));

        results.AddRange(itemsToAdd);

        for (var i = 0; i < results.Count; i++)
        {
            results[i].SortedEntry.SortIndex = i;
        }

        return results;
    }
}
