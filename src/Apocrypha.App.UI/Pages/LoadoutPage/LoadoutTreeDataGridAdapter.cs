using System.ComponentModel;
using Avalonia.Controls.Models.TreeDataGrid;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Pages.LibraryPage;
using NexusMods.MnemonicDB.Abstractions;
using OneOf;
using R3;

namespace Apocrypha.App.UI.Pages.LoadoutPage;

public readonly record struct ToggleEnableStateMessage(LoadoutItemId[] Ids);
public readonly record struct OpenCollectionMessage(LoadoutItemId[] Ids, NavigationInformation NavigationInformation);
public readonly record struct ViewModPageMessage(LoadoutItemId[] Ids);
public readonly record struct ViewModFilesMessage(LoadoutItemId[] Ids, NavigationInformation NavigationInformation);
public readonly record struct UninstallItemMessage(LoadoutItemId[] Ids);
public readonly record struct MoveToCollectionMessage(LoadoutItemId[] Ids, CollectionGroupId TargetCollection);

public class LoadoutTreeDataGridAdapter :
    TreeDataGridAdapter<CompositeItemModel<EntityId>, EntityId>,
    ITreeDataGirdMessageAdapter<OneOf<ToggleEnableStateMessage, OpenCollectionMessage, ViewModPageMessage, ViewModFilesMessage, UninstallItemMessage, MoveToCollectionMessage>>
{
    public Subject<OneOf<ToggleEnableStateMessage, OpenCollectionMessage, ViewModPageMessage, ViewModFilesMessage, UninstallItemMessage, MoveToCollectionMessage>> MessageSubject { get; } = new();

    private readonly ILoadoutDataProvider[] _loadoutDataProviders;
    private readonly LoadoutFilter _loadoutFilter;
    private readonly IConnection _connection;

    public LoadoutTreeDataGridAdapter(IServiceProvider serviceProvider, LoadoutFilter loadoutFilter) : base(serviceProvider)
    {
        _loadoutDataProviders = serviceProvider.GetServices<ILoadoutDataProvider>().ToArray();
        _loadoutFilter = loadoutFilter;
        _connection = serviceProvider.GetRequiredService<IConnection>();
    }

    protected override IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> GetRootsObservable(bool viewHierarchical)
    {
        return _loadoutDataProviders.Select(x => x.ObserveLoadoutItems(_loadoutFilter)).MergeChangeSets();
    }

    protected override void BeforeModelActivationHook(CompositeItemModel<EntityId> model)
    {
        base.BeforeModelActivationHook(model);

        model.SubscribeToComponentAndTrack<LoadoutComponents.EnabledStateToggle, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.EnabledStateToggleComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandToggle.Subscribe((self, itemModel, component), static (_, tuple) =>
                {
                    var (self, itemModel, _) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new ToggleEnableStateMessage(ids));
                }
            )
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.ParentCollectionDisabled, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.ParentCollectionDisabledComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.ButtonCommand.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
                {
                    var (self, itemModel, _) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new OpenCollectionMessage(ids, navInfo));
                }
            )
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.LockedEnabledState, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.LockedEnabledStateComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.ButtonCommand.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
                {
                    var (self, itemModel, _) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new OpenCollectionMessage(ids, navInfo));
                }
            )
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.MixLockedAndParentDisabled, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.MixLockedAndParentDisabledComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.ButtonCommand.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
                {
                    var (self, itemModel, _) = tuple;
                    var ids = GetLoadoutItemIds(itemModel).ToArray();

                    self.MessageSubject.OnNext(new OpenCollectionMessage(ids, navInfo));
                }
            )
        );
        
        model.SubscribeToComponentAndTrack<SharedComponents.ViewModPageAction, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.ViewModPageComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandViewModPage.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, _) = state;
                var ids = GetLoadoutItemIds(model).ToArray();

                self.MessageSubject.OnNext(new ViewModPageMessage(ids));
            })
        );
        
        model.SubscribeToComponentAndTrack<SharedComponents.ViewModFilesAction, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.ViewModFilesComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.Command.Subscribe((self, itemModel, component), static (navInfo, tuple) =>
            {
                var (self, model, _) = tuple;
                var ids = GetLoadoutItemIds(model).ToArray();

                self.MessageSubject.OnNext(new ViewModFilesMessage(ids, navInfo));
            })
        );
        
        model.SubscribeToComponentAndTrack<SharedComponents.UninstallItemAction, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.UninstallItemComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandUninstallItem.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, _) = state;
                var ids = GetLoadoutItemIds(model).ToArray();

                self.MessageSubject.OnNext(new UninstallItemMessage(ids));
            })
        );

        model.SubscribeToComponentAndTrack<LoadoutComponents.MoveToCollectionAction, LoadoutTreeDataGridAdapter>(
            key: LoadoutColumns.EnabledState.MoveToCollectionComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.MoveRequests.Subscribe((self, itemModel, component), static (targetCollection, state) =>
            {
                var (self, model, _) = state;
                var ids = GetLoadoutItemIds(model).ToArray();

                self.MessageSubject.OnNext(new MoveToCollectionMessage(ids, targetCollection));
            })
        );
    }

    private static IEnumerable<LoadoutItemId> GetLoadoutItemIds(CompositeItemModel<EntityId> itemModel)
    {
        return itemModel.Get<LoadoutComponents.LoadoutItemIds>(LoadoutColumns.EnabledState.LoadoutItemIdsComponentKey).ItemIds;
    }

    protected override IColumn<CompositeItemModel<EntityId>>[] CreateColumns(bool viewHierarchical)
    {
        var nameColumn = ColumnCreator.Create<EntityId, SharedColumns.Name>();

        return
        [
            viewHierarchical ? ITreeDataGridItemModel<CompositeItemModel<EntityId>, EntityId>.CreateExpanderColumn(nameColumn) : nameColumn,
            ColumnCreator.Create<EntityId, LibraryColumns.ItemVersion>(),
            ColumnCreator.Create<EntityId, SharedColumns.InstalledDate>(sortDirection: ListSortDirection.Descending),
            ColumnCreator.Create<EntityId, LoadoutColumns.Collections>(),
            ColumnCreator.Create<EntityId, LoadoutColumns.EnabledState>(),
        ];
    }

    private bool _isDisposed;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed)
        {
            MessageSubject.Dispose();
            _isDisposed = true;
        }

        base.Dispose(disposing);
    }
}
