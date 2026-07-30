using System.Collections.Frozen;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.NexusModsApi;
using R3;
using OneOf;

namespace Apocrypha.Abstractions.Games;

/// <summary>
/// Implementation for ISortOrderManager
/// Responsible for subscribing and updating sortOrders for all registered varieties when loadouts or collections change.
/// </summary>
public class SortOrderManager : ISortOrderManager, IDisposable
{
    private readonly IServiceProvider _serviceProvider; 
    private readonly IConnection _connection;
    private IDisposable? _subscription;
    private readonly ILogger<SortOrderManager> _logger;
    private readonly TimeProvider _timeProvider;

    private FrozenDictionary<SortOrderVarietyId, ISortOrderVariety> _sortOrderVarieties;

    public SortOrderManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _connection = _serviceProvider.GetRequiredService<IConnection>();
        _sortOrderVarieties = FrozenDictionary<SortOrderVarietyId, ISortOrderVariety>.Empty;
        _logger = _serviceProvider.GetRequiredService<ILogger<SortOrderManager>>();
        // Drives the reconcile-batching window; overridable so tests don't wait on wall-clock.
        _timeProvider = _serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask UpdateLoadOrders(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId = default, CancellationToken token = default)
    {
        var parentEntity = collectionGroupId.HasValue
            ? OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId.Value)
            : OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId);
        
        foreach (var sortOrderVariety in _sortOrderVarieties.Values)
        {
            // Reconcile the sort order for each variety
            var sortOrderId = await sortOrderVariety.GetOrCreateSortOrderFor(loadoutId, parentEntity, token);
            await sortOrderVariety.ReconcileSortOrder(sortOrderId, token: token);
        }
    }
    
    protected async ValueTask CreateSortOrders(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId = default, CancellationToken token = default)
    {
        var parentEntity = collectionGroupId.HasValue
            ? OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId.Value)
            : OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId);
        
        foreach (var sortOrderVariety in _sortOrderVarieties.Values)
        {
            // Create the sort order for each variety
            _ = await sortOrderVariety.GetOrCreateSortOrderFor(loadoutId, parentEntity, token);
        }
    }

    protected async ValueTask DeleteSortOrders(LoadoutId loadoutId, Optional<CollectionGroupId> collectionGroupId = default, CancellationToken token = default)
    {
        var parentEntity = collectionGroupId.HasValue
            ? OneOf<LoadoutId, CollectionGroupId>.FromT1(collectionGroupId.Value)
            : OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutId);
        
        foreach (var sortOrderVariety in _sortOrderVarieties.Values)
        {
            // Delete the sort order for each variety
            var sortOrderId = sortOrderVariety.GetSortOrderIdFor(parentEntity);
            if (!sortOrderId.HasValue) continue;
            
            await sortOrderVariety.DeleteSortOrder(sortOrderId.Value, token: token);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ISortOrderVariety> GetSortOrderVarieties()
    {
        return _sortOrderVarieties.Values;
    }

    /// <inheritdoc />
    public void RegisterSortOrderVarieties(ISortOrderVariety[] sortOrderVarieties, IGame game)
    {
        _sortOrderVarieties = sortOrderVarieties.ToDictionary(variety => variety.SortOrderVarietyId)
            .ToFrozenDictionary();
        
        if (_sortOrderVarieties.Count > 0)
        {
            // Subscribe to changes in the sort orders
            SubscribeToChanges(game.NexusModsGameId.Value);
        }
    }

    protected void SubscribeToChanges(NexusModsGameId nexusModsGameId)
    {
        var compositeDisposable = new CompositeDisposable();
        var conn = _connection;
 
        
        // Listen to Loadouts additions/removals
        Loadout.ObserveAll(_connection)
            .StartWithEmpty()
            .FilterImmutable(l => l.Installation.GameId == nexusModsGameId)
            .ToObservable()
            .SubscribeAwait(this, static async (changes, state, token) =>
                {
                    foreach (var change in changes)
                    {
                        var loadoutId = change.Current.LoadoutId;
                        var parentEntity = loadoutId;

                        switch (change.Reason)
                        {
                            case ChangeReason.Add:
                                // Create the sort order for this loadout
                                // Might not be needed if the other subscription to loadout items handles it
                                await state.CreateSortOrders(loadoutId, token: token);
                                break;
                            case ChangeReason.Update:
                                // If loadout changes, we handle that in a separate subscription
                                break;
                            case ChangeReason.Remove:
                                // Remove the orphaned sort orders
                                await state.DeleteSortOrders(loadoutId, token: token);
                                break;
                        }
                    }
                }
            )
            .AddTo(compositeDisposable);
        
        
        // Listen to collection groups additions/removals
        CollectionGroup.ObserveAll(_connection)
            .StartWithEmpty()
            .FilterImmutable(cg => cg.AsLoadoutItemGroup().AsLoadoutItem().Loadout.Installation.GameId == nexusModsGameId)
            .ToObservable()
            .SubscribeAwait(this, static async (changes, state, token) =>
                {
                    foreach (var change in changes)
                    {
                        var collectionGroupId = change.Current.CollectionGroupId;
                        var loadoutId = change.Current.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId;
                        var parentEntity = Optional.Some(collectionGroupId);

                        switch (change.Reason)
                        {
                            case ChangeReason.Add:
                                // Create the sort order for this collection group
                                // Might not be needed if the other subscription to loadout items handles it
                                await state.CreateSortOrders(loadoutId, parentEntity, token: token);
                                break;
                            case ChangeReason.Update:
                                // If collection group changes, we handle that in a separate subscription
                                break;
                            case ChangeReason.Remove:
                                // Remove the orphaned sort orders
                                await state.DeleteSortOrders(loadoutId, parentEntity, token: token);
                                break;
                        }
                    }
                }
            )
            .AddTo(compositeDisposable);
        

        // Listen to item additions/removals
        // TODO: consider making this Variant specific, to only listen to changes for the relevant items
        // TODO: listen to changes to Game files too
        //
        // Reconciliation is a full, idempotent recomputation, so a burst of commits only needs one
        // of them. Gather the affected targets over a short window instead of reconciling per
        // commit -- see SortOrderReconcileBatching for the measurements behind that.
        var itemChanges = SortOrderQueries.TrackLoadoutItemChanges(_connection, nexusModsGameId)
            .ToObservable()
            .Select(static changes =>
            {
                // Updates never move an item in or out of a sort order.
                var targets = new List<ReconcileTarget>();
                foreach (var change in changes)
                {
                    if (change.Reason == ChangeReason.Update) continue;

                    var loadoutId = change.Current.LoadoutId;
                    targets.Add(new ReconcileTarget(loadoutId, EntityId.From(0)));

                    var collectionId = change.Current.CollectionId;
                    if (collectionId != 0)
                        targets.Add(new ReconcileTarget(loadoutId, collectionId));
                }

                return (IReadOnlyList<ReconcileTarget>)targets;
            });

        SortOrderReconcileBatching
            .Batch(itemChanges, SortOrderReconcileBatching.ReconcileWindow, _timeProvider)
            .SubscribeAwait(this, static async (targets, state, token) =>
            {
                foreach (var target in targets)
                {
                    var collectionId = target.IsCollectionLevel
                        ? Optional.Some(CollectionGroupId.From(target.CollectionId))
                        : Optional<CollectionGroupId>.None;

                    await state.UpdateLoadOrders(target.LoadoutId, collectionId, token: token);
                }
            })
            .AddTo(compositeDisposable);
        
        _subscription = compositeDisposable;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}


