using System.Reactive.Linq;
using DynamicData;
using DynamicData.Aggregation;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Abstractions.Thunderstore.Models;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Pages.LibraryPage;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.Query;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;
using UIObservableExtensions = Apocrypha.App.UI.Extensions.ObservableExtensions;

namespace Apocrypha.App.UI.Pages;

/// <summary>
/// Library/loadout data provider for items downloaded from Thunderstore. Modeled on
/// <see cref="LocalFileDataProvider"/>, plus the version column from the Thunderstore
/// version metadata.
/// </summary>
[UsedImplicitly]
internal class ThunderstoreDataProvider : ILibraryDataProvider, ILoadoutDataProvider
{
    private readonly IConnection _connection;

    public ThunderstoreDataProvider(IServiceProvider serviceProvider)
    {
        _connection = serviceProvider.GetRequiredService<IConnection>();
    }

    // TODO: return files once Thunderstore items gain a game association (design doc §9.4 / PR D)
    public LibraryFile.ReadOnly[] GetAllFiles(GameId gameId, IDb? db = null) => [];

    public IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> ObserveLibraryItems(LibraryFilter libraryFilter)
    {
        return ObserveGameLibraryItems(libraryFilter)
            .Transform(item => ToLibraryItemModel(libraryFilter, item));
    }

    public IObservable<int> CountLibraryItems(LibraryFilter libraryFilter)
    {
        return ObserveGameLibraryItems(libraryFilter)
            .QueryWhenChanged(query => query.Count)
            .Prepend(0);
    }

    /// <summary>
    /// Thunderstore packages are global, so the Library scopes them by the package's community
    /// listings: a game without a Thunderstore community shows none, and a package whose
    /// listings are still unknown (awaiting the startup backfill) stays visible everywhere
    /// rather than vanishing. Listings arriving from the backfill refresh the filter live.
    /// </summary>
    private IObservable<IChangeSet<ThunderstoreLibraryItem.ReadOnly, EntityId>> ObserveGameLibraryItems(LibraryFilter libraryFilter)
    {
        var allItems = ThunderstoreLibraryItem.ObserveAll(_connection);

        if (libraryFilter.Game is not IThunderstoreCommunityGame thunderstoreGame)
            return allItems.Filter(static _ => false);

        var communitySlug = thunderstoreGame.ThunderstoreCommunitySlug;
        return allItems
            .AutoRefreshOnObservable(item => _connection.ObserveDatoms(item.Version.PackageId).Skip(1))
            .Filter(item =>
            {
                // Re-load against the current db: the item snapshot predates backfill writes.
                var package = ThunderstoreLibraryItem.Load(_connection.Db, item.Id).Version.Package;
                var communities = package.Communities;
                return !communities.Any() || communities.Contains(communitySlug);
            });
    }

    private CompositeItemModel<EntityId> ToLibraryItemModel(LibraryFilter libraryFilter, ThunderstoreLibraryItem.ReadOnly item)
    {
        var linkedLoadoutItemsObservable = LibraryDataProviderHelper
            .GetLinkedLoadoutItems(_connection, libraryFilter, item.Id)
            .RefCount();

        var childrenObservable = UIObservableExtensions.ReturnFactory(() =>
        {
            var itemModel = new CompositeItemModel<EntityId>(item.Id);
            SetupLibraryItemModel(itemModel, item, linkedLoadoutItemsObservable);

            return new ChangeSet<CompositeItemModel<EntityId>, EntityId>([
                new Change<CompositeItemModel<EntityId>, EntityId>(
                    reason: ChangeReason.Add,
                    key: item.Id,
                    current: itemModel
                )]
            );
        });

        var hasChildrenObservable = childrenObservable.IsNotEmpty();

        var parentItemModel = new CompositeItemModel<EntityId>(item.Id)
        {
            HasChildrenObservable = hasChildrenObservable,
            ChildrenObservable = childrenObservable,
        };

        SetupLibraryItemModel(parentItemModel, item, linkedLoadoutItemsObservable);

        // TODO: load the real package icon (ThunderstorePackageMetadata.IconUri) via a resource pipeline
        parentItemModel.Add(SharedColumns.Name.ImageComponentKey, new ImageComponent(value: ImagePipelines.ModPageThumbnailFallback));
        return parentItemModel;
    }

    private static void SetupLibraryItemModel(
        CompositeItemModel<EntityId> itemModel,
        ThunderstoreLibraryItem.ReadOnly item,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedLoadoutItemsObservable)
    {
        var libraryItem = item.AsLibraryItem();

        itemModel.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: libraryItem.Name));
        itemModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(value: item.Version.VersionNumber));
        itemModel.Add(LibraryColumns.DownloadedDate.ComponentKey, new DateComponent(value: item.GetCreatedAt()));
        if (libraryItem.TryGetAsLibraryFile(out var libraryFile))
            itemModel.Add(SharedColumns.ItemSize.ComponentKey, new SizeComponent(value: libraryFile.Size));
        itemModel.Add(LibraryColumns.Actions.LibraryItemIdsComponentKey, new LibraryComponents.LibraryItemIds(libraryItem));

        LibraryDataProviderHelper.AddInstalledDateComponent(itemModel, linkedLoadoutItemsObservable);
        LibraryDataProviderHelper.AddInstallActionComponent(itemModel, linkedLoadoutItemsObservable);
        LibraryDataProviderHelper.AddViewChangelogActionComponent(itemModel, isEnabled: false);
        LibraryDataProviderHelper.AddViewModPageActionComponent(itemModel, isEnabled: false);
        LibraryDataProviderHelper.AddHideUpdatesActionComponent(itemModel, isEnabled: false, isVisible: false);
        LibraryDataProviderHelper.AddRelatedCollectionsComponent(itemModel, linkedLoadoutItemsObservable);
    }

    private IObservable<IChangeSet<ThunderstoreLibraryItem.ReadOnly, EntityId>> FilterLoadoutItems(LoadoutFilter loadoutFilter)
    {
        return ThunderstoreLibraryItem
            .ObserveAll(_connection)
            .FilterOnObservable((_, entityId) => _connection
                .ObserveDatoms(LibraryLinkedLoadoutItem.LibraryItemId, entityId)
                .AsEntityIds()
                .FilterInStaticLoadout(_connection, loadoutFilter)
                .IsNotEmpty()
            );
    }

    public IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> ObserveLoadoutItems(LoadoutFilter loadoutFilter)
    {
        return FilterLoadoutItems(loadoutFilter).Transform(item => ToLoadoutItemModel(loadoutFilter, item));
    }

    public IObservable<int> CountLoadoutItems(LoadoutFilter loadoutFilter)
    {
        return FilterLoadoutItems(loadoutFilter).QueryWhenChanged(static query => query.Count).Prepend(0);
    }

    private CompositeItemModel<EntityId> ToLoadoutItemModel(LoadoutFilter loadoutFilter, ThunderstoreLibraryItem.ReadOnly item)
    {
        var linkedItemsObservable = _connection.ObserveDatoms(LibraryLinkedLoadoutItem.LibraryItem, item)
            .AsEntityIds()
            .FilterInStaticLoadout(_connection, loadoutFilter)
            .Transform(datom => LoadoutItem.Load(_connection.Db, datom.E))
            .RefCount();

        var hasChildrenObservable = linkedItemsObservable.IsNotEmpty();
        var childrenObservable = linkedItemsObservable.Transform(loadoutItem => ToThunderstoreChildLoadoutItemModel(_connection, loadoutItem, item.Version.VersionNumber));

        var parentItemModel = new CompositeItemModel<EntityId>(item.Id)
        {
            HasChildrenObservable = hasChildrenObservable,
            ChildrenObservable = childrenObservable,
        };

        parentItemModel.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: item.AsLibraryItem().Name));
        parentItemModel.Add(SharedColumns.Name.ImageComponentKey, new ImageComponent(value: ImagePipelines.ModPageThumbnailFallback));
        parentItemModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(value: item.Version.VersionNumber));

        LoadoutDataProviderHelper.AddDateComponent(parentItemModel, item.GetCreatedAt(), linkedItemsObservable);
        LoadoutDataProviderHelper.AddCollections(parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddParentCollectionsDisabled(_connection, parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddMixLockedAndParentDisabled(_connection, parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddLockedEnabledStates(parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddEnabledStateToggle(_connection, parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddLoadoutItemIds(parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddViewModPageActionComponent(parentItemModel, isEnabled: false);
        LoadoutDataProviderHelper.AddViewModFilesActionComponent(parentItemModel, linkedItemsObservable);
        LoadoutDataProviderHelper.AddUninstallItemComponent(parentItemModel, linkedItemsObservable);

        return parentItemModel;
    }

    private static CompositeItemModel<EntityId> ToThunderstoreChildLoadoutItemModel(IConnection connection, LoadoutItem.ReadOnly loadoutItem, string versionNumber)
    {
        var childModel = LoadoutDataProviderHelper.ToChildItemModel(connection, loadoutItem);
        LoadoutDataProviderHelper.AddViewModPageActionComponent(childModel, isEnabled: false);
        childModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(value: versionNumber));
        return childModel;
    }
}
