using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModIo.Models;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Extensions;
using Apocrypha.App.UI.Pages.LibraryPage;
using Apocrypha.Sdk.Resources;
using Apocrypha.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.Query;
using NexusMods.Paths;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;

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
    private readonly Lazy<IResourceLoader<EntityId, Bitmap>> _iconLoader;

    public ModIoDataProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _connection = serviceProvider.GetRequiredService<IConnection>();
        _iconLoader = new Lazy<IResourceLoader<EntityId, Bitmap>>(() => ImagePipelines.GetModIoIconPipeline(serviceProvider));
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

    /// <summary>
    /// One row per mod, rolled up across downloaded files (layout epic PR L3) — grouping the
    /// already-scoped item stream by mod identity, rather than re-deriving the game-slug filter
    /// top-down, keeps <see cref="ObserveGameLibraryItems"/>'s scoping logic untouched.
    /// </summary>
    public IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> ObserveLibraryItems(LibraryFilter libraryFilter)
    {
        return ObserveGameLibraryItems(libraryFilter)
            .Group(item => item.File.ModId.Value)
            .Transform(group => ToModLibraryItemModel(group, libraryFilter));
    }

    public IObservable<int> CountLibraryItems(LibraryFilter libraryFilter)
    {
        return ObserveGameLibraryItems(libraryFilter)
            .Group(item => item.File.ModId.Value)
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

    private CompositeItemModel<EntityId> ToModLibraryItemModel(IGroup<ModIoLibraryItem.ReadOnly, EntityId, EntityId> group, LibraryFilter libraryFilter)
    {
        var mod = ModIoModMetadata.Load(_connection.Db, group.Key);
        var libraryItems = group.Cache.Connect().RefCount();

        var linkedLoadoutItemsObservable = libraryItems.MergeManyChangeSets(item => LibraryDataProviderHelper.GetLinkedLoadoutItems(_connection, libraryFilter, item.Id));

        var hasChildrenObservable = libraryItems.IsNotEmpty();
        var childrenObservable = libraryItems.Transform(item => ToFileLibraryItemModel(libraryFilter, item));

        var parentItemModel = new CompositeItemModel<EntityId>(group.Key)
        {
            HasChildrenObservable = hasChildrenObservable,
            ChildrenObservable = childrenObservable,
        };

        parentItemModel.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: mod.Name));
        parentItemModel.Add(SharedColumns.Name.ImageComponentKey, ImageComponent.FromPipeline(_iconLoader.Value, group.Key, initialValue: ImagePipelines.ModPageThumbnailFallback));
        parentItemModel.Add(SharedColumns.Name.SourceIconComponentKey, new UnifiedIconComponent(IconValues.ModIo));

        // Size: sum of downloaded files
        var sizeObservable = libraryItems
            .TransformImmutable(static item => LibraryFile.Size.GetOptional(item).ValueOr(() => Size.Zero))
            .ForAggregation()
            .Sum(static size => (long)size.Value)
            .Select(static size => Size.FromLong(size));

        parentItemModel.Add(SharedColumns.ItemSize.ComponentKey, new SizeComponent(
            initialValue: Size.Zero,
            valueObservable: sizeObservable
        ));

        // Downloaded date: most recent downloaded file's date
        var downloadedDateObservable = libraryItems
            .TransformImmutable(static item => item.GetCreatedAt())
            .QueryWhenChanged(query => query.Items.OptionalMaxBy(item => item).ValueOr(DateTimeOffset.MinValue));

        parentItemModel.Add(LibraryColumns.DownloadedDate.ComponentKey, new DateComponent(
            initialValue: mod.GetCreatedAt(),
            valueObservable: downloadedDateObservable
        ));

        // Version: from the most recently published downloaded file. mod.io file versions are
        // free-form and not always present, and UploadedAt is optional too — fall back to
        // download recency for ordering so a version-less file still contributes correctly.
        var currentVersionObservable = libraryItems
            .TransformImmutable(static item =>
            {
                ModIoFileMetadata.Version.TryGetValue(item.File, out var version);
                var sortKey = ModIoFileMetadata.UploadedAt.GetOptional(item.File).ValueOr(() => item.GetCreatedAt());
                return (sortKey, version: version ?? string.Empty);
            })
            .QueryWhenChanged(static query => query.Items
                .OptionalMaxBy(static tuple => tuple.sortKey)
                .Convert(static tuple => tuple.version)
                .ValueOr(string.Empty));

        parentItemModel.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey, new VersionComponent(
            initialValue: string.Empty,
            valueObservable: currentVersionObservable
        ));

        LibraryDataProviderHelper.AddInstalledDateComponent(parentItemModel, linkedLoadoutItemsObservable);
        LibraryDataProviderHelper.AddViewChangelogActionComponent(parentItemModel, isEnabled: false);
        LibraryDataProviderHelper.AddViewModPageActionComponent(parentItemModel, isEnabled: false);
        LibraryDataProviderHelper.AddHideUpdatesActionComponent(parentItemModel, isEnabled: false, isVisible: false);
        LibraryDataProviderHelper.AddRelatedCollectionsComponent(parentItemModel, linkedLoadoutItemsObservable);

        parentItemModel.Add(LibraryColumns.Actions.LibraryItemIdsComponentKey, new LibraryComponents.LibraryItemIds(libraryItems.TransformImmutable(static x => x.AsLibraryItem().LibraryItemId)));

        var matchesObservable = libraryItems
            .TransformOnObservable(item => LibraryDataProviderHelper.GetLinkedLoadoutItems(_connection, libraryFilter, item.Id).IsNotEmpty())
            .QueryWhenChanged(query =>
            {
                var (numInstalled, numTotal) = (0, 0);
                foreach (var isInstalled in query.Items)
                {
                    numInstalled += isInstalled ? 1 : 0;
                    numTotal++;
                }

                return new MatchesData(numInstalled, numTotal);
            });
        LibraryDataProviderHelper.AddInstallActionComponent(parentItemModel, matchesObservable);

        return parentItemModel;
    }

    private CompositeItemModel<EntityId> ToFileLibraryItemModel(LibraryFilter libraryFilter, ModIoLibraryItem.ReadOnly item)
    {
        var linkedLoadoutItemsObservable = LibraryDataProviderHelper
            .GetLinkedLoadoutItems(_connection, libraryFilter, item.Id)
            .RefCount();

        var itemModel = new CompositeItemModel<EntityId>(item.Id);
        SetupLibraryItemModel(itemModel, item, linkedLoadoutItemsObservable);
        return itemModel;
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
