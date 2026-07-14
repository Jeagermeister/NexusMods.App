using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using DynamicData;
using DynamicData.Binding;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.App.UI.Extensions;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Controls.Spine.Buttons;
using Apocrypha.App.UI.Controls.Spine.Buttons.Download;
using Apocrypha.App.UI.Controls.Spine.Buttons.Icon;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image.LoadoutFlyout;
using Apocrypha.App.UI.LeftMenu;
using Apocrypha.App.UI.Pages.Downloads;
using Apocrypha.App.UI.Pages.HomeDashboard;
using Apocrypha.App.UI.Pages.LoadoutPage;
using Apocrypha.App.UI.Pages.MyGames;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceAttachments;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.Query;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.Games;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.Controls.Spine;

[UsedImplicitly]
public class SpineViewModel : AViewModel<ISpineViewModel>, ISpineViewModel
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpineViewModel> _logger;
    private readonly IWindowManager _windowManager;
    private readonly ISynchronizerService _syncService;

    private ReadOnlyObservableCollection<IImageButtonViewModel> _loadoutSpineItems = new([]);
    private static readonly GameSpineEntriesComparer GameComparerInstance = new();
    public ReadOnlyObservableCollection<IImageButtonViewModel> LoadoutSpineItems => _loadoutSpineItems;
    public IIconButtonViewModel Home { get; }
    public IIconButtonViewModel AddLoadout { get; }
    public ISpineDownloadButtonViewModel Downloads { get; }
    private IList<ISpineItemViewModel> _specialSpineItems = new List<ISpineItemViewModel>();

    private ISpineItemViewModel? _activeSpineItem;

    private Dictionary<WorkspaceId, ILeftMenuViewModel> _leftMenus = new([]);
    private readonly IConnection _conn;
    private readonly Apocrypha.Sdk.Games.IGameRegistry _gameRegistryForFilter;
    private readonly ILoadoutManager _loadoutManager;
    [Reactive] public ILeftMenuViewModel? LeftMenuViewModel { get; private set; }

    public SpineViewModel(
        IServiceProvider serviceProvider,
        ILogger<SpineViewModel> logger,
        IConnection conn,
        IWindowManager windowManager,
        IIconButtonViewModel addButtonViewModel,
        IIconButtonViewModel homeButtonViewModel,
        ISpineDownloadButtonViewModel spineDownloadsButtonViewModel,
        IWorkspaceAttachmentsFactoryManager workspaceAttachmentsFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _windowManager = windowManager;
        _conn = conn;
        _gameRegistryForFilter = serviceProvider.GetRequiredService<Apocrypha.Sdk.Games.IGameRegistry>();
        _syncService = serviceProvider.GetRequiredService<ISynchronizerService>();
        _loadoutManager = serviceProvider.GetRequiredService<ILoadoutManager>();

        // Setup the special spine items
        Home = homeButtonViewModel;
        Home.Name = Language.SpineHomeButton_ToolTip_Home;
        Home.WorkspaceContext = new HomeContext();
        _specialSpineItems.Add(Home);
        Home.Click = ReactiveCommand.Create(NavigateToHome);

        AddLoadout = addButtonViewModel;
        AddLoadout.Name = "Add";
        AddLoadout.WorkspaceContext = new HomeContext();
        _specialSpineItems.Add(AddLoadout);
        AddLoadout.Click = ReactiveCommand.Create(NavigateToMyGames);

        Downloads = spineDownloadsButtonViewModel;
        Downloads.WorkspaceContext = new DownloadsContext();
        _specialSpineItems.Add(Downloads);
        Downloads.Click = ReactiveCommand.Create(NavigateToDownloads);

        var workspaceController = windowManager.ActiveWorkspaceController;

        this.WhenActivated(disposables =>
            {
                var loadouts = Loadout.ObserveAll(_conn);

                var visibleLoadouts = loadouts
                    // Orphaned loadouts (game uninstalled/moved) must not fault the spine pipeline
                    .Filter(loadout => loadout.IsVisible() && _gameRegistryForFilter.TryGetGameInstallation(loadout, out _))
                    // Rebuild a game's spine button whenever one of its loadouts' own attributes
                    // change (e.g. LastAppliedDateTime on apply) so ordering/click-target stay current.
                    .AutoRefreshOnObservable(loadout => _conn.ObserveDatoms(loadout.Id).Skip(1));

                visibleLoadouts
                    .Group(loadout => loadout.InstallationId)
                    .TransformAsync(group => BuildGameSpineButton(group, workspaceController, disposables))
                    .OnUI()
                    // ActivityTimestamp is updated in-place (see ApplyLoadoutGroupMembers) rather
                    // than via a fresh changeset event, so re-sorting needs an explicit nudge.
                    .AutoRefresh(vm => vm.ActivityTimestamp)
                    .SortAndBind(out _loadoutSpineItems, GameComparerInstance)
                    .SubscribeWithErrorLogging()
                    .DisposeWith(disposables);

            // Create Left Menus for each workspace on demand
            workspaceController.AllWorkspaces
                .ToObservableChangeSet()
                .OnItemAdded(workspace =>
                {
                    if (_leftMenus.TryGetValue(workspace.Id, out _))
                    {
                        return;
                    }
                        
                    try
                    {
                        var leftMenu = workspaceAttachmentsFactory.CreateLeftMenuFor(
                            workspace.Context,
                            workspace.Id,
                            workspaceController
                        );

                        if (leftMenu == null)
                        {
                            throw new InvalidDataException("LeftMenu factory returned a null view model");
                        }
                        
                        _leftMenus.Add(workspace.Id, leftMenu);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Exception while creating left menu for context {Context}", workspace.Context);
                    }
                })
                .OnItemRemoved(workspace => _leftMenus.Remove(workspace.Id, out _))
                .SubscribeWithErrorLogging()
                .DisposeWith(disposables);

            // Unregister a removed loadout's own workspace (independent of whether its game's
            // spine button survives — the button persists as long as the game has another
            // loadout) and navigate away if it was the active workspace.
            loadouts
                .OnUI()
                .OnItemRemoved(loadout =>
                    {
                        workspaceController.UnregisterWorkspaceByContext<LoadoutContext>(context => context.LoadoutId == loadout.LoadoutId);

                        if (workspaceController.ActiveWorkspace.Context is LoadoutContext activeLoadoutContext &&
                            activeLoadoutContext.LoadoutId == loadout.LoadoutId)
                        {
                            workspaceController.ChangeOrCreateWorkspaceByContext<HomeContext>(() => new PageData
                                {
                                    FactoryId = MyGamesPageFactory.StaticId,
                                    Context = new MyGamesPageContext(),
                                }
                            );
                        }
                    }, false
                )
                .SubscribeWithErrorLogging()
                .DisposeWith(disposables);

            // Update the LeftMenuViewModel when the active workspace changes
            workspaceController.WhenAnyValue(controller => controller.ActiveWorkspace)
                .Select(workspace => workspace.Id)
                .Select(workspaceId => _leftMenus.GetValueOrDefault(workspaceId))
                .BindToVM(this, vm => vm.LeftMenuViewModel)
                .DisposeWith(disposables);

            // Update the active spine item when the active workspace changes
            workspaceController
                .WhenAnyValue(controller => controller.ActiveWorkspace)
                .Select(workspace => workspace.Context)
                .WhereNotNull()
                .SubscribeWithErrorLogging(context =>
                    {
                        var itemToActivate = _specialSpineItems
                            .Concat(_loadoutSpineItems)
                            .FirstOrDefault(spineItem => spineItem.WorkspaceContext?.Equals(context) == true);

                        SetActiveItem(itemToActivate);
                    }
                )
                .DisposeWith(disposables);
            
            }
        );
    }

    private void SetActiveItem(ISpineItemViewModel? itemToActivate)
    {
        if (itemToActivate == null)
            return;

        if (_activeSpineItem != null)
            _activeSpineItem.IsActive = false;

        itemToActivate.IsActive = true;
        _activeSpineItem = itemToActivate;
    }

    /// <summary>
    /// Builds one spine button per managed game (layout epic PR L4). All group members share the
    /// same <see cref="Loadout.Installation"/>, so icon/game identity come from any member; the
    /// button opens the group's most recently active loadout on click, and — when the game has
    /// more than one loadout — exposes them all (+ "New loadout") via a flyout. The icon is loaded
    /// once (it never changes for a given installation), but the rest of the button's state is
    /// re-applied on every membership/refresh signal from the group's live cache — <c>Group()</c>
    /// re-emitting the outer group on membership changes can't be relied on to re-trigger this
    /// method, so the reactivity has to live inside it.
    /// </summary>
    private async Task<IImageButtonViewModel> BuildGameSpineButton(
        IGroup<Loadout.ReadOnly, EntityId, GameInstallMetadataId> group,
        IWorkspaceController workspaceController,
        CompositeDisposable disposables)
    {
        var anyLoadout = group.Cache.Items.First();
        var installation = anyLoadout.InstallationInstance;

        await using var iconStream = await installation.Game.IconImage.GetStreamAsync();

        var vm = _serviceProvider.GetRequiredService<IImageButtonViewModel>();
        vm.Image = LoadImageFromStream(iconStream);
        vm.IsActive = false;

        group.Cache.Connect()
            .ToCollection()
            .OnUI()
            .SubscribeWithErrorLogging(members => ApplyLoadoutGroupMembers(vm, members.ToArray(), installation, workspaceController))
            .DisposeWith(disposables);

        return vm;
    }

    private void ApplyLoadoutGroupMembers(
        IImageButtonViewModel vm,
        Loadout.ReadOnly[] members,
        GameInstallation installation,
        IWorkspaceController workspaceController)
    {
        // Empty means the group is being torn down (game unmanaged) — the outer pipeline's
        // OnItemRemoved-equivalent (SortAndBind reacting to the group's removal) drops this
        // button shortly; nothing useful to apply in the meantime.
        if (members.Length == 0) return;

        var primaryLoadout = members.OrderByDescending(EffectiveActivity).First();

        vm.Name = installation.Game.DisplayName + " - " + primaryLoadout.Name;
        vm.LoadoutBadgeViewModel ??= new LoadoutBadgeViewModel(_conn, _syncService, hideOnSingleLoadout: true);
        vm.LoadoutBadgeViewModel.LoadoutValue = primaryLoadout;
        vm.WorkspaceContext = new LoadoutContext { LoadoutId = primaryLoadout.LoadoutId };
        vm.Click = ReactiveCommand.Create(() => ChangeToLoadoutWorkspace(primaryLoadout.LoadoutId));
        vm.ActivityTimestamp = EffectiveActivity(primaryLoadout);
        vm.HasMultipleLoadouts = members.Length > 1;

        if (members.Length > 1)
        {
            var flyoutItems = new ObservableCollection<ILoadoutFlyoutItemViewModel>(
                members
                    .OrderBy(loadout => loadout.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(BuildFlyoutItem)
            );
            vm.Loadouts = new ReadOnlyObservableCollection<ILoadoutFlyoutItemViewModel>(flyoutItems);
            vm.CreateNewLoadoutCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                await _loadoutManager.CreateLoadout(installation);
            });
        }
        else
        {
            vm.Loadouts = null;
        }

        if (workspaceController.ActiveWorkspace.Context is LoadoutContext activeLoadoutContext &&
            members.Any(loadout => loadout.LoadoutId == activeLoadoutContext.LoadoutId))
        {
            SetActiveItem(vm);
        }
    }

    private ILoadoutFlyoutItemViewModel BuildFlyoutItem(Loadout.ReadOnly loadout)
    {
        return new LoadoutFlyoutItemViewModel(loadout, _serviceProvider)
        {
            VisitLoadoutCommand = ReactiveCommand.Create(() => ChangeToLoadoutWorkspace(loadout.LoadoutId)),
        };
    }

    /// <summary>
    /// A loadout's most recent activity: when it was last applied, falling back to when it was
    /// created for a loadout that's never been applied.
    /// </summary>
    private static DateTimeOffset EffectiveActivity(Loadout.ReadOnly loadout)
    {
        return loadout.LastAppliedDateTime.TryGet(out var lastApplied) ? lastApplied : loadout.GetCreatedAt();
    }

    private Bitmap LoadImageFromStream(Stream iconStream)
    {
        try
        {
            return Bitmap.DecodeToWidth(iconStream, (int) ImageSizes.GameThumbnail.Width);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skia image load error, while loading image from stream");
            // Null images are fine, they will be ignored
            return null!;
        }
    }

    public void NavigateToHome()
    {
        var workspaceController = _windowManager.ActiveWorkspaceController;

        var pageData = new PageData
        {
            FactoryId = HomeDashboardPageFactory.StaticId,
            Context = new HomeDashboardPageContext(),
        };

        // ChangeOrCreateWorkspaceByContext only opens pageData when it creates a brand new
        // HomeContext workspace — an existing one (e.g. from before this page existed, still
        // showing My Games) is just brought to focus as-is. Force the dashboard open explicitly,
        // same as NavigateToMyGames does for the exact same reason.
        var ws = workspaceController.ChangeOrCreateWorkspaceByContext<HomeContext>(() => pageData);
        var behavior = workspaceController.GetOpenPageBehavior(pageData, NavigationInformation.From(NavigationInput.Default));
        workspaceController.OpenPage(ws.Id, pageData, behavior);
    }

    private void ChangeToLoadoutWorkspace(LoadoutId loadoutId)
    {
        var workspaceController = _windowManager.ActiveWorkspaceController;

        workspaceController.ChangeOrCreateWorkspaceByContext(
            context => context.LoadoutId == loadoutId,
            () => new PageData
            {
                FactoryId = LoadoutPageFactory.StaticId,
                Context = new LoadoutPageContext
                {
                    LoadoutId = loadoutId,
                    GroupScope = Optional<CollectionGroupId>.None,
                }
            },
            () => new LoadoutContext
            {
                LoadoutId = loadoutId
            }
        );
    }

    private void NavigateToMyGames()
    {
        var workspaceController = _windowManager.ActiveWorkspaceController;

        var pageData = new PageData
        {
            FactoryId = MyGamesPageFactory.StaticId,
            Context = new MyGamesPageContext(),
        };

        var ws = workspaceController.ChangeOrCreateWorkspaceByContext<HomeContext>(() => pageData);
        var behavior = workspaceController.GetOpenPageBehavior(pageData, NavigationInformation.From(NavigationInput.Default));
        workspaceController.OpenPage(ws.Id, pageData, behavior);
    }

    private void NavigateToDownloads()
    {
        var workspaceController = _windowManager.ActiveWorkspaceController;

        workspaceController.ChangeOrCreateWorkspaceByContext<DownloadsContext>(() => new PageData
            {
                FactoryId = DownloadsPageFactory.StaticId,
                Context = new DownloadsPageContext { GameScope = Optional<GameId>.None }
            }
        );
    }

    private class GameSpineEntriesComparer : IComparer<IImageButtonViewModel>
    {
        public int Compare(IImageButtonViewModel? x, IImageButtonViewModel? y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;

            // Most recently active game first ("jump back in" ordering).
            return DateTimeOffset.Compare(y.ActivityTimestamp, x.ActivityTimestamp);
        }
    }
}
