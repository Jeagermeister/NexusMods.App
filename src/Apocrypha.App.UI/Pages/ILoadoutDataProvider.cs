using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Extensions;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Extensions;
using Apocrypha.App.UI.Pages.LoadoutPage;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.DatomIterators;
using NexusMods.MnemonicDB.Abstractions.IndexSegments;
using NexusMods.MnemonicDB.Abstractions.Models;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;
using R3;

namespace Apocrypha.App.UI.Pages;

public interface ILoadoutDataProvider
{
    IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> ObserveLoadoutItems(LoadoutFilter loadoutFilter);

    IObservable<int> CountLoadoutItems(LoadoutFilter loadoutFilter);
}

public class LoadoutFilter
{
    public required LoadoutId LoadoutId { get; init; }
    public required Optional<LoadoutItemGroupId> CollectionGroupId { get; init; }
}

public static class LoadoutDataProviderHelper
{
    public static IObservable<int> CountAllLoadoutItems(IServiceProvider serviceProvider, LoadoutId loadoutId)
    {
        return CountAllLoadoutItems(serviceProvider, new LoadoutFilter
        {
            LoadoutId = loadoutId,
            CollectionGroupId = Optional<LoadoutItemGroupId>.None,
        });
    }

    public static IObservable<int> CountAllLoadoutItems(IServiceProvider serviceProvider, LoadoutFilter loadoutFilter)
    {
        var loadoutDataProviders = serviceProvider.GetServices<ILoadoutDataProvider>();
        return loadoutDataProviders
            .Select(provider => provider.CountLoadoutItems(loadoutFilter))
            .CombineLatest(static counts => counts.Sum());
    }

    public static ChangeSet<LoadoutItem.ReadOnly, EntityId> GetLinkedLoadoutItems(
        IDb db,
        LoadoutFilter loadoutFilter,
        LibraryItemId libraryItemId)
    {
        List<EntityId> entityIds;

        if (loadoutFilter.CollectionGroupId.HasValue)
        {
            entityIds = db.Datoms(
                (LibraryLinkedLoadoutItem.LibraryItemId, libraryItemId),
                (LoadoutItem.ParentId, loadoutFilter.CollectionGroupId.Value)
            );
        }
        else
        {
            entityIds = db.Datoms(
                (LibraryLinkedLoadoutItem.LibraryItemId, libraryItemId),
                (LoadoutItem.LoadoutId, loadoutFilter.LoadoutId)
            );
        }

        var changeSet = new ChangeSet<LoadoutItem.ReadOnly, EntityId>();

        foreach (var entityId in entityIds)
        {
            var item = LoadoutItem.Load(db, entityId);
            if (!item.IsValid()) continue;

            changeSet.Add(new Change<LoadoutItem.ReadOnly, EntityId>(ChangeReason.Add, entityId, item));
        }

        return changeSet;
    }

    public static CompositeItemModel<EntityId> ToChildItemModel(IConnection connection, LoadoutItem.ReadOnly loadoutItem)
    {
        var itemModel = new CompositeItemModel<EntityId>(loadoutItem.Id);

        itemModel.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: loadoutItem.Name));
        itemModel.Add(SharedColumns.InstalledDate.ComponentKey, new DateComponent(value: loadoutItem.GetCreatedAt()));
        itemModel.Add(LoadoutColumns.EnabledState.LoadoutItemIdsComponentKey, new LoadoutComponents.LoadoutItemIds(itemId: loadoutItem));
        itemModel.Add(LoadoutColumns.EnabledState.ViewModFilesComponentKey, new SharedComponents.ViewModFilesAction(isEnabled: true));

        AddCollection(connection, itemModel, loadoutItem);
        AddParentCollectionDisabled(connection, itemModel, loadoutItem);
        AddLockedEnabledState(itemModel, loadoutItem);
        AddEnabledStateToggle(connection, itemModel, loadoutItem);
        AddUninstallItemComponent(itemModel, loadoutItem);
        AddMoveToCollectionComponent(connection, loadoutItem.LoadoutId, itemModel, loadoutItem);

        return itemModel;
    }

    public static void AddCollection(IConnection connection, CompositeItemModel<EntityId> itemModel, LoadoutItem.ReadOnly loadoutItem)
    {
        if (!loadoutItem.HasParent() || !loadoutItem.Parent.TryGetAsCollectionGroup(out var collectionGroup)) return;

        // Observe the item so the column follows "Move to collection" reparents
        var nameObservable = LoadoutItem.Observe(connection, loadoutItem.Id)
            .Select(static item => item.HasParent() && item.Parent.TryGetAsCollectionGroup(out var group)
                ? group.AsLoadoutItemGroup().AsLoadoutItem().Name
                : string.Empty
            );

        itemModel.Add(LoadoutColumns.Collections.ComponentKey, new StringComponent(
            initialValue: collectionGroup.AsLoadoutItemGroup().AsLoadoutItem().Name,
            valueObservable: nameObservable
        ));
    }

    public static void AddParentCollectionDisabled(IConnection connection, CompositeItemModel<EntityId> itemModel, LoadoutItem.ReadOnly loadoutItem)
    {
        if (!loadoutItem.HasParent() || !loadoutItem.Parent.TryGetAsCollectionGroup(out var collectionGroup)) return;

        var isParentCollectionDisabledObservable = LoadoutItem.Observe(connection, collectionGroup.Id).Select(static item => item.IsDisabled).ToObservable();

        itemModel.AddObservable(
            key: LoadoutColumns.EnabledState.ParentCollectionDisabledComponentKey,
            shouldAddObservable: isParentCollectionDisabledObservable,
            componentFactory: () => new LoadoutComponents.ParentCollectionDisabled()
        );
    }

    public static void AddParentCollectionsDisabled(
        IConnection connection,
        CompositeItemModel<EntityId> parentItemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        // Check if all children have a disabled parent collection
        var isParentCollectionDisabledObservable = linkedItemsObservable
            .TransformOnObservable(item =>
                {
                    if (!item.HasParent() || !item.Parent.TryGetAsCollectionGroup(out var collectionGroup))
                        return System.Reactive.Linq.Observable.Return(false);
                    return LoadoutItem.Observe(connection, collectionGroup)
                        .Select(static parentItem => parentItem.IsDisabled);
                }
            )
            .QueryWhenChanged(query => query.Items.All(isDisabled => isDisabled))
            .ToObservable();

        parentItemModel.AddObservable(
            key: LoadoutColumns.EnabledState.ParentCollectionDisabledComponentKey,
            shouldAddObservable: isParentCollectionDisabledObservable,
            componentFactory: () => new LoadoutComponents.ParentCollectionDisabled()
        );
    }

    public static void AddLockedEnabledState(CompositeItemModel<EntityId> itemModel, LoadoutItem.ReadOnly loadoutItem)
    {
        if (IsLocked(loadoutItem))
            itemModel.Add(LoadoutColumns.EnabledState.LockedEnabledStateComponentKey, new LoadoutComponents.LockedEnabledState());
    }

    public static void AddEnabledStateToggle(IConnection connection, CompositeItemModel<EntityId> itemModel, LoadoutItem.ReadOnly loadoutItem)
    {
        var isEnabledObservable = LoadoutItem.Observe(connection, loadoutItem.Id).Select(static item => (bool?)!item.IsDisabled);
        itemModel.Add(LoadoutColumns.EnabledState.EnabledStateToggleComponentKey,
            new LoadoutComponents.EnabledStateToggle(
                valueComponent: new ValueComponent<bool?>(
                    initialValue: !loadoutItem.IsDisabled,
                    valueObservable: isEnabledObservable
        )));
    }
    
    public static void AddViewModPageActionComponent(
        CompositeItemModel<EntityId> itemModel,
        bool isEnabled = true)
    {
        itemModel.Add(LoadoutColumns.EnabledState.ViewModPageComponentKey, new SharedComponents.ViewModPageAction(isEnabled));
    }
    
    public static void AddDateComponent(
        CompositeItemModel<EntityId> parentItemModel,
        DateTimeOffset initialValue,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        var dateObservable = linkedItemsObservable
            .QueryWhenChanged(query => query.Items
                .Select(static item => item.GetCreatedAt())
                .OptionalMinBy(item => item)
                .ValueOr(DateTimeOffset.MinValue)
            );

        parentItemModel.Add(SharedColumns.InstalledDate.ComponentKey, new DateComponent(
            initialValue: initialValue,
            valueObservable: dateObservable
        ));
    }
    
    public static void AddViewModFilesActionComponent(
        CompositeItemModel<EntityId> itemModel,  
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        // Always show, will open the first mod page if multiple
        itemModel.Add(LoadoutColumns.EnabledState.ViewModFilesComponentKey, new SharedComponents.ViewModFilesAction(isEnabled: true));
    }

    public static void AddUninstallItemComponent(CompositeItemModel<EntityId> itemModel, LoadoutItem.ReadOnly loadoutItem)
    {
        var canDelete = !IsLocked(loadoutItem);
        itemModel.Add(LoadoutColumns.EnabledState.UninstallItemComponentKey, new SharedComponents.UninstallItemAction(isEnabled: canDelete));
    }
    
    public static void AddUninstallItemComponent(CompositeItemModel<EntityId> itemModel, IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
       var canUninstallObservable = linkedItemsObservable
           .TransformImmutable(static item => IsLocked(item))
           // Show uninstall if at least one item is not locked
           .QueryWhenChanged(static query => !query.Items.All(isLocked => isLocked))
           .ToObservable();

       itemModel.Add(LoadoutColumns.EnabledState.UninstallItemComponentKey,
           new SharedComponents.UninstallItemAction(canUninstallObservable)
       );
    }

    public static void AddMoveToCollectionComponent(
        IConnection connection,
        LoadoutId loadoutId,
        CompositeItemModel<EntityId> itemModel,
        LoadoutItem.ReadOnly loadoutItem)
    {
        var rowStateObservable = LoadoutItem.Observe(connection, loadoutItem.Id)
            .Select(static item => IsCollectionManaged(item)
                ? (MovableParents: Array.Empty<EntityId>(), HasMovableItems: false)
                : (MovableParents: item.HasParent() ? new[] { item.ParentId.Value } : Array.Empty<EntityId>(), HasMovableItems: true)
            );

        AddMoveToCollectionComponent(connection, loadoutId, itemModel, rowStateObservable);
    }

    public static void AddMoveToCollectionComponent(
        IConnection connection,
        LoadoutId loadoutId,
        CompositeItemModel<EntityId> itemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        var rowStateObservable = linkedItemsObservable
            // Observe each item so the submenu targets follow reparents live
            .TransformOnObservable(item => LoadoutItem.Observe(connection, item.Id)
                .Select(static live => (IsMovable: !IsCollectionManaged(live), Parent: live.HasParent() ? live.ParentId.Value : default(EntityId?)))
            )
            .QueryWhenChanged(static query =>
            {
                var movableParents = query.Items
                    .Where(static tuple => tuple.IsMovable && tuple.Parent.HasValue)
                    .Select(static tuple => tuple.Parent!.Value)
                    .Distinct()
                    .ToArray();

                var hasMovableItems = query.Items.Any(static tuple => tuple.IsMovable);
                return (MovableParents: movableParents, HasMovableItems: hasMovableItems);
            });

        AddMoveToCollectionComponent(connection, loadoutId, itemModel, rowStateObservable);
    }

    private static void AddMoveToCollectionComponent(
        IConnection connection,
        LoadoutId loadoutId,
        CompositeItemModel<EntityId> itemModel,
        IObservable<(EntityId[] MovableParents, bool HasMovableItems)> rowStateObservable)
    {
        var targetsObservable = LoadoutQueries2.MutableCollections(connection, loadoutId)
            .Observe(static r => r.GroupId)
            .QueryWhenChanged(static query => query.Items
                .OrderBy(static r => r.GroupId.Value)
                .Select(static r => (Id: CollectionGroupId.From(r.GroupId), r.Name))
                .ToArray()
            );

        var stateObservable = targetsObservable.CombineLatest(rowStateObservable, static (targets, rowState) =>
        {
            // Hide a target when the row's movable items already all live there
            var visibleTargets = rowState.MovableParents.Length == 1
                ? targets.Where(target => !target.Id.Value.Equals(rowState.MovableParents[0])).ToArray()
                : targets;

            return new LoadoutComponents.MoveToCollectionState(visibleTargets, rowState.HasMovableItems);
        });

        itemModel.Add(LoadoutColumns.EnabledState.MoveToCollectionComponentKey, new LoadoutComponents.MoveToCollectionAction(stateObservable));
    }

    /// <summary>
    /// Whether the item was put into the loadout by a collection (required or optional) —
    /// such items stay with their collection and can't be moved.
    /// </summary>
    public static bool IsCollectionManaged<T>(T entity) where T : struct, IReadOnlyModel<T>
    {
        return NexusCollectionItemLoadoutGroup.IsRequired.GetOptional(entity).HasValue;
    }

    public static void AddCollections(
        IConnection connection,
        CompositeItemModel<EntityId> parentItemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        // Observe each item so the column follows "Move to collection" reparents
        var collectionsObservable = linkedItemsObservable
            .TransformOnObservable(item => LoadoutItem.Observe(connection, item.Id)
                .Select(static live => live.HasParent() && live.Parent.TryGetAsCollectionGroup(out var group)
                    ? group.AsLoadoutItemGroup().AsLoadoutItem().Name
                    : string.Empty
                )
            )
            .QueryWhenChanged(query => query.Items
                .Where(static name => name.Length != 0)
                .Distinct()
                .Order(StringComparer.OrdinalIgnoreCase)
                .SafeAggregate(defaultValue: string.Empty, static (a, b) => $"{a}, {b}")
            );

        parentItemModel.Add(LoadoutColumns.Collections.ComponentKey, new StringComponent(
            initialValue: string.Empty,
            valueObservable: collectionsObservable
        ));
    }
    
    public static void AddLockedEnabledStates(
        CompositeItemModel<EntityId> parentItemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        var isLockedObservable = linkedItemsObservable
            .TransformImmutable(static item => IsLocked(item))
            .QueryWhenChanged(static query => query.Items.All(isLocked => isLocked))
            .ToObservable();

        parentItemModel.AddObservable(
            key: LoadoutColumns.EnabledState.LockedEnabledStateComponentKey,
            shouldAddObservable: isLockedObservable,
            componentFactory: () => new LoadoutComponents.LockedEnabledState()
        );
    }
    
    public static void AddMixLockedAndParentDisabled(
        IConnection connection,
        CompositeItemModel<EntityId> parentItemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        var shouldAddObservable = linkedItemsObservable
            .TransformOnObservable(item =>
                {
                    var isLocked = IsLocked(item);
                    if (!item.HasParent() || !item.Parent.TryGetAsCollectionGroup(out var collectionGroup))
                        return System.Reactive.Linq.Observable.Return((IsLocked: isLocked, IsParentDisabled: false));
                    
                    return LoadoutItem.Observe(connection, collectionGroup)
                        .Select(parentItem => (IsLocked: isLocked, IsParentDisabled: parentItem.IsDisabled));
                }
            )
            .QueryWhenChanged(query =>
            {
                // Check if all items are either locked or parent disabled, but we need to have at least one of each
                var hasLocked = false;
                var hasParentDisabled = false;
                foreach (var (isLocked, isParentDisabled) in query.Items)
                {
                    if (isParentDisabled) hasParentDisabled = true;
                    // locked state only counts if the parent is not disabled
                    if (isLocked && !isParentDisabled) hasLocked = true;
                    if (!isLocked && !isParentDisabled) return false;
                }
                return hasLocked && hasParentDisabled;
            })
            .ToObservable();

        parentItemModel.AddObservable(
            key: LoadoutColumns.EnabledState.MixLockedAndParentDisabledComponentKey,
            shouldAddObservable: shouldAddObservable,
            componentFactory: () => new LoadoutComponents.MixLockedAndParentDisabled()
        );
    }

    public static void AddEnabledStateToggle(
        IConnection connection,
        CompositeItemModel<EntityId> parentItemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        // The toggle click skips collection-required (locked) and parent-disabled copies, so
        // its VALUE must be aggregated the same way: a mod page carrying both a collection's
        // locked copy and the user's own copy otherwise renders locked/indeterminate and the
        // user's copy looks un-toggleable (collection + own mods coexistence, §46).
        var isEnabledObservable = linkedItemsObservable
            .TransformOnObservable(item =>
            {
                var isLocked = IsLocked(item);
                var isEnabledObservable = item.IsEnabledObservable(connection);

                if (!item.HasParent() || !item.Parent.TryGetAsCollectionGroup(out var collectionGroup))
                    return isEnabledObservable.Select(isEnabled => (IsToggleable: !isLocked, IsEnabled: isEnabled));

                return LoadoutItem.Observe(connection, collectionGroup)
                    .CombineLatest(isEnabledObservable, (parentItem, isEnabled) =>
                        (IsToggleable: !isLocked && !parentItem.IsDisabled, IsEnabled: isEnabled));
            })
            .QueryWhenChanged(query =>
            {
                static bool? Aggregate(IEnumerable<bool> states)
                {
                    var isEnabled = Optional<bool>.None;
                    foreach (var state in states)
                    {
                        if (!isEnabled.HasValue) isEnabled = state;
                        else if (isEnabled.Value != state) return null;
                    }
                    return isEnabled.HasValue ? isEnabled.Value : null;
                }

                var toggleable = query.Items.Where(tuple => tuple.IsToggleable).Select(tuple => tuple.IsEnabled).ToArray();
                if (toggleable.Length != 0) return Aggregate(toggleable);

                // Nothing toggleable (fully locked/disabled rows are covered by the lock and
                // parent-disabled components) — keep the historical all-items aggregate.
                return Aggregate(query.Items.Select(tuple => tuple.IsEnabled));
            });

        parentItemModel.Add(LoadoutColumns.EnabledState.EnabledStateToggleComponentKey, new LoadoutComponents.EnabledStateToggle(
            valueComponent: new ValueComponent<bool?>(
                initialValue: true,
                valueObservable: isEnabledObservable
            )
        ));
    }
    
    public static void AddLoadoutItemIds(
        CompositeItemModel<EntityId> parentItemModel,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedItemsObservable)
    {
        var loadoutItemIdsObservable = linkedItemsObservable
            .TransformImmutable(static item => item.LoadoutItemId);

        parentItemModel.Add(LoadoutColumns.EnabledState.LoadoutItemIdsComponentKey, new LoadoutComponents.LoadoutItemIds(loadoutItemIdsObservable));
    }

    public static IObservable<IChangeSet<Datom, EntityId>> FilterInStaticLoadout(
        this IObservable<IChangeSet<Datom, EntityId>> source,
        IConnection connection,
        LoadoutFilter loadoutFilter)
    {
        var filterByCollection = loadoutFilter.CollectionGroupId.HasValue;
        return source.Filter(datom =>
        {
            var item = LoadoutItem.Load(connection.Db, datom.E);
            if (!item.LoadoutId.Equals(loadoutFilter.LoadoutId)) return false;
            if (filterByCollection) 
                return item.IsChildOf(loadoutFilter.CollectionGroupId.Value);
            return true;
        });
    }

    public static bool IsLocked<T>(T entity) where T : struct, IReadOnlyModel<T>
    {
        return NexusCollectionItemLoadoutGroup.IsRequired.GetOptional(entity).ValueOr(false);
    }
}
