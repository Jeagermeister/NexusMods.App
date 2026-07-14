using System.Reactive.Linq;
using DynamicData;
using DynamicData.Aggregation;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModIo.Models;
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
/// Library/loadout data provider for items downloaded from mod.io. Modeled on
/// <see cref="ThunderstoreDataProvider"/>, but simpler: a mod.io mod belongs to exactly
/// one game, so scoping is a static slug comparison (DESIGN-modio.md decision 4) with no
/// backfill machinery.
/// </summary>
[UsedImplicitly]
internal class ModIoDataProvider : ILibraryDataProvider, ILoadoutDataProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;

    public ModIoDataProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _connection = serviceProvider.GetRequiredService<IConnection>();
    }

    /// <summary>
    /// The library files this game owns on mod.io — used by "Remove game &amp; delete downloads" to
    /// clean up archives. A mod.io mod belongs to exactly one game, so ownership is an exact
    /// <see cref="ModIoModMetadata.GameNameId"/> match (the same scoping the Library view uses).
    /// </summary>
    public LibraryFile.ReadOnly[] GetAllFiles(GameId gameId, IDb? db = null)
    {
        var game = _serviceProvider.GetServices<IGameData>().FirstOrDefault(x => x.GameId == gameId);
        if (game is not IModIoGame modIoGame) return [];

        var gameNameId = modIoGame.ModIoGameNameId;
        db ??= _connection.Db;

        var files = new List<LibraryFile.ReadOnly>();
        foreach (var item in ModIoLibraryItem.All(db))
        {
            if (!string.Equals(item.File.Mod.GameNameId, gameNameId, StringComparison.OrdinalIgnoreCase)) continue;
            if (item.AsLibraryItem().TryGetAsLibraryFile(out var file)) files.Add(file);
        }

        return files.ToArray();
    }

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

    private IObservable<IChangeSet<ModIoLibraryItem.ReadOnly, EntityId>> ObserveGameLibraryItems(LibraryFilter libraryFilter)
    {
        var allItems = ModIoLibraryItem.ObserveAll(_connection);

        if (libraryFilter.Game is not IModIoGame modIoGame)
            return allItems.Filter(static _ => false);

        var gameNameId = modIoGame.ModIoGameNameId;
        return allItems.Filter(item => string.Equals(item.File.Mod.GameNameId, gameNameId, StringComparison.OrdinalIgnoreCase));
    }

    private CompositeItemModel<EntityId> ToLibraryItemModel(LibraryFilter libraryFilter, ModIoLibraryItem.ReadOnly item)
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

        // TODO: load the real mod logo (ModIoModMetadata.LogoUri) via a resource pipeline
        parentItemModel.Add(SharedColumns.Name.ImageComponentKey, new ImageComponent(value: ImagePipelines.ModPageThumbnailFallback));
        return parentItemModel;
    }

    private static void SetupLibraryItemModel(
        CompositeItemModel<EntityId> itemModel,
        ModIoLibraryItem.ReadOnly item,
        IObservable<IChangeSet<LoadoutItem.ReadOnly, EntityId>> linkedLoadoutItemsObservable)
    {
        var libraryItem = item.AsLibraryItem();

        itemModel.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: libraryItem.Name));
        if (ModIoFileMetadata.Version.TryGetValue(item.File, out var version))
            itemModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(value: version));
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

    private IObservable<IChangeSet<ModIoLibraryItem.ReadOnly, EntityId>> FilterLoadoutItems(LoadoutFilter loadoutFilter)
    {
        return ModIoLibraryItem
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

    private CompositeItemModel<EntityId> ToLoadoutItemModel(LoadoutFilter loadoutFilter, ModIoLibraryItem.ReadOnly item)
    {
        var linkedItemsObservable = _connection.ObserveDatoms(LibraryLinkedLoadoutItem.LibraryItem, item)
            .AsEntityIds()
            .FilterInStaticLoadout(_connection, loadoutFilter)
            .Transform(datom => LoadoutItem.Load(_connection.Db, datom.E))
            .RefCount();

        var hasChildrenObservable = linkedItemsObservable.IsNotEmpty();
        ModIoFileMetadata.Version.TryGetValue(item.File, out var versionNumber);
        var childrenObservable = linkedItemsObservable.Transform(loadoutItem => ToModIoChildLoadoutItemModel(_connection, loadoutItem, versionNumber));

        var parentItemModel = new CompositeItemModel<EntityId>(item.Id)
        {
            HasChildrenObservable = hasChildrenObservable,
            ChildrenObservable = childrenObservable,
        };

        parentItemModel.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: item.AsLibraryItem().Name));
        parentItemModel.Add(SharedColumns.Name.ImageComponentKey, new ImageComponent(value: ImagePipelines.ModPageThumbnailFallback));
        if (versionNumber is not null)
            parentItemModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(value: versionNumber));

        LoadoutDataProviderHelper.AddDateComponent(parentItemModel, item.GetCreatedAt(), linkedItemsObservable);
        LoadoutDataProviderHelper.AddCollections(_connection, parentItemModel, linkedItemsObservable);
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

    private static CompositeItemModel<EntityId> ToModIoChildLoadoutItemModel(IConnection connection, LoadoutItem.ReadOnly loadoutItem, string? versionNumber)
    {
        var childModel = LoadoutDataProviderHelper.ToChildItemModel(connection, loadoutItem);
        LoadoutDataProviderHelper.AddViewModPageActionComponent(childModel, isEnabled: false);
        if (versionNumber is not null)
            childModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(value: versionNumber));
        return childModel;
    }
}
