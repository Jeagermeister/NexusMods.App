using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Extensions;
using Apocrypha.App.UI.Pages.LoadoutPage;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.Query;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

/// <summary>
/// The Home dashboard (layout epic PR L5): "jump back in" tiles for every managed game plus a
/// recent-activity feed, derived entirely from existing MnemonicDB data — no new persistence.
/// </summary>
[UsedImplicitly]
public class HomeDashboardViewModel : APageViewModel<IHomeDashboardViewModel>, IHomeDashboardViewModel
{
    /// <summary>
    /// How many recent activity rows to show. The design doc leaves exact depth/retention open;
    /// this is a tunable constant, not an architectural choice.
    /// </summary>
    private const int ActivityFeedDepth = 20;

    private readonly IConnection _conn;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISynchronizerService _syncService;
    private readonly IWindowManager _windowManager;
    private readonly IGameRegistry _gameRegistryForFilter;

    private ReadOnlyObservableCollection<IDashboardGameTileViewModel> _gameTiles = new([]);
    public ReadOnlyObservableCollection<IDashboardGameTileViewModel> GameTiles => _gameTiles;

    // The feed is capped to ActivityFeedDepth, which DynamicData's Bind(out ...) doesn't support
    // directly — a fixed backing collection is cleared/repopulated in place on each update instead.
    private readonly ObservableCollection<IActivityFeedItemViewModel> _activityFeedBacking = [];
    private readonly ReadOnlyObservableCollection<IActivityFeedItemViewModel> _activityFeed;
    public ReadOnlyObservableCollection<IActivityFeedItemViewModel> ActivityFeed => _activityFeed;

    public HomeDashboardViewModel(
        IWindowManager windowManager,
        IServiceProvider serviceProvider,
        IConnection conn,
        ISynchronizerService syncService,
        IGameRegistry gameRegistry) : base(windowManager)
    {
        _activityFeed = new ReadOnlyObservableCollection<IActivityFeedItemViewModel>(_activityFeedBacking);
        _windowManager = windowManager;
        _serviceProvider = serviceProvider;
        _conn = conn;
        _syncService = syncService;
        _gameRegistryForFilter = gameRegistry;

        TabTitle = Language.HomeWorkspace_Title;
        TabIcon = IconValues.Apocrypha;

        this.WhenActivated(d =>
        {
            var visibleLoadouts = Loadout.ObserveAll(_conn)
                // Orphaned loadouts (game uninstalled/moved) must not fault the dashboard —
                // same defensive filter the spine uses (PR L4).
                .Filter(loadout => loadout.IsVisible() && _gameRegistryForFilter.TryGetGameInstallation(loadout, out _))
                // Rebuild a tile's in-place state whenever one of its loadouts' own attributes
                // change (e.g. LastAppliedDateTime on apply) — see ApplyGameTileMembers.
                .AutoRefreshOnObservable(loadout => _conn.ObserveDatoms(loadout.Id).Skip(1));

            visibleLoadouts
                .Group(loadout => loadout.InstallationId)
                .Transform(group => BuildGameTile(group, d))
                .OnUI()
                // ActivityTimestamp is updated in place (group membership changes don't reliably
                // re-trigger Group()/TransformAsync — same finding as the PR L4 spine grouping),
                // so re-sorting needs an explicit nudge.
                .AutoRefresh(vm => vm.ActivityTimestamp)
                .SortAndBind(out _gameTiles, GameTileComparer.Instance)
                .SubscribeWithErrorLogging()
                .DisposeWith(d);

            // Orphaned/unresolvable references (a stale or system group whose Loadout attribute
            // doesn't resolve) must not fault the whole feed — same defensive stance as the
            // spine's own "orphaned loadouts" filter (PR L4).
            var installEvents = LoadoutItemGroup.ObserveAll(_conn)
                .Transform(TryBuildInstallEvent)
                .Filter(item => item.HasValue)
                .Transform(item => item.Value);

            var applyEvents = Loadout.ObserveAll(_conn)
                .Transform(TryBuildApplyEvent)
                .Filter(item => item.HasValue)
                .Transform(item => item.Value);

            installEvents.Merge(applyEvents)
                .OnUI()
                .QueryWhenChanged(query => query.Items.OrderByDescending(item => item.Timestamp).Take(ActivityFeedDepth).ToArray())
                .SubscribeWithErrorLogging(items =>
                {
                    _activityFeedBacking.Clear();
                    foreach (var item in items) _activityFeedBacking.Add(item);
                })
                .DisposeWith(d);
        });
    }

    private IDashboardGameTileViewModel BuildGameTile(
        IGroup<Loadout.ReadOnly, EntityId, GameInstallMetadataId> group,
        CompositeDisposable disposables)
    {
        var anyLoadout = group.Cache.Items.First();

        var gameWidget = _serviceProvider.GetRequiredService<IGameWidgetViewModel>();
        gameWidget.Installation = anyLoadout.InstallationInstance;
        gameWidget.IsManagedObservable = Observable.Return(true);

        var tile = new DashboardGameTileViewModel(gameWidget, _syncService);

        // GameWidgetViewModel/DashboardGameTileViewModel derive their reactive properties (e.g.
        // GameWidget's Name/Version) inside their own WhenActivated blocks, which normally only
        // run once a View activates them. The dashboard binds straight to those properties
        // without using GameWidget's own View, so they need to be activated by hand — and that
        // has to happen on the UI thread (WhenActivated bindings assert it), while this method
        // itself runs on DynamicData's Transform, i.e. off the UI thread. Defer it.
        Dispatcher.UIThread.Post(() =>
        {
            gameWidget.Activator.Activate().DisposeWith(disposables);
            tile.Activator.Activate().DisposeWith(disposables);
        });

        group.Cache.Connect()
            .ToCollection()
            .OnUI()
            .SubscribeWithErrorLogging(members => ApplyGameTileMembers(tile, gameWidget, members.ToArray()))
            .DisposeWith(disposables);

        return tile;
    }

    private void ApplyGameTileMembers(IDashboardGameTileViewModel tile, IGameWidgetViewModel gameWidget, Loadout.ReadOnly[] members)
    {
        if (members.Length == 0) return;

        var primaryLoadout = members.OrderByDescending(EffectiveActivity).First();

        tile.PrimaryLoadoutId = primaryLoadout.LoadoutId;
        tile.ActivityTimestamp = EffectiveActivity(primaryLoadout);
        gameWidget.ViewGameCommand = ReactiveCommand.Create(() => NavigateToLoadout(primaryLoadout.LoadoutId));
    }

    private Optional<IActivityFeedItemViewModel> TryBuildInstallEvent(LoadoutItemGroup.ReadOnly group)
    {
        try
        {
            var item = group.AsLoadoutItem();
            if (item.HasParent()) return Optional<IActivityFeedItemViewModel>.None;
            if (!item.Loadout.IsVisible()) return Optional<IActivityFeedItemViewModel>.None;

            var loadoutId = item.Loadout.LoadoutId;
            return new ActivityFeedItemViewModel
            {
                Icon = IconValues.ModsOutline,
                Text = string.Format(Language.HomeDashboard_ActivityFeed_Installed, item.Name),
                Timestamp = group.GetCreatedAt(),
                NavigateCommand = ReactiveCommand.Create(() => NavigateToLoadout(loadoutId)),
            };
        }
        catch (KeyNotFoundException)
        {
            // An orphaned/unresolvable Loadout reference — skip this row rather than fault
            // the whole feed.
            return Optional<IActivityFeedItemViewModel>.None;
        }
    }

    private Optional<IActivityFeedItemViewModel> TryBuildApplyEvent(Loadout.ReadOnly loadout)
    {
        try
        {
            if (!loadout.IsVisible()) return Optional<IActivityFeedItemViewModel>.None;
            if (!loadout.LastAppliedDateTime.TryGet(out var appliedAt)) return Optional<IActivityFeedItemViewModel>.None;
            if (!_gameRegistryForFilter.TryGetGameInstallation(loadout, out _)) return Optional<IActivityFeedItemViewModel>.None;

            var loadoutId = loadout.LoadoutId;
            var gameName = loadout.InstallationInstance.Game.DisplayName;

            return new ActivityFeedItemViewModel
            {
                Icon = IconValues.CheckBold,
                Text = string.Format(Language.HomeDashboard_ActivityFeed_Applied, gameName, loadout.Name),
                Timestamp = appliedAt,
                NavigateCommand = ReactiveCommand.Create(() => NavigateToLoadout(loadoutId)),
            };
        }
        catch (KeyNotFoundException)
        {
            return Optional<IActivityFeedItemViewModel>.None;
        }
    }

    private void NavigateToLoadout(LoadoutId loadoutId)
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
                },
            },
            () => new LoadoutContext
            {
                LoadoutId = loadoutId,
            }
        );
    }

    /// <summary>
    /// A loadout's most recent activity: when it was last applied, falling back to when it was
    /// created for a loadout that's never been applied. Mirrors the spine's identical helper
    /// (PR L4) so tile and spine ordering agree on what "recent" means.
    /// </summary>
    private static DateTimeOffset EffectiveActivity(Loadout.ReadOnly loadout)
    {
        return loadout.LastAppliedDateTime.TryGet(out var lastApplied) ? lastApplied : loadout.GetCreatedAt();
    }

    private class GameTileComparer : IComparer<IDashboardGameTileViewModel>
    {
        public static readonly GameTileComparer Instance = new();

        public int Compare(IDashboardGameTileViewModel? x, IDashboardGameTileViewModel? y)
        {
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;

            return DateTimeOffset.Compare(y.ActivityTimestamp, x.ActivityTimestamp);
        }
    }
}
