using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Helpers;
using Apocrypha.App.UI.LeftMenu.Items;
using Apocrypha.App.UI.Pages.Downloads;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;
using R3;

namespace Apocrypha.App.UI.LeftMenu.Downloads;

[UsedImplicitly]
public class DownloadsLeftMenuViewModel : AViewModel<IDownloadsLeftMenuViewModel>, IDownloadsLeftMenuViewModel
{
    public WorkspaceId WorkspaceId { get; }
    public ILeftMenuItemViewModel LeftMenuItemAllDownloads { get; }

    private ReadOnlyObservableCollection<ILeftMenuItemViewModel> _leftMenuItemsPerGameDownloads = new([]);
    public ReadOnlyObservableCollection<ILeftMenuItemViewModel> LeftMenuItemsPerGameDownloads => _leftMenuItemsPerGameDownloads;

    public DownloadsLeftMenuViewModel(
        WorkspaceId workspaceId,
        IWorkspaceController workspaceController,
        IServiceProvider serviceProvider)
    {
        WorkspaceId = workspaceId;
        var logger = serviceProvider.GetRequiredService<ILogger<DownloadsLeftMenuViewModel>>();
        var connection = serviceProvider.GetRequiredService<IConnection>();
        var gameRegistry = serviceProvider.GetRequiredService<IGameRegistry>();

        // All Downloads menu item
        LeftMenuItemAllDownloads = new LeftMenuItemViewModel(
            workspaceController,
            WorkspaceId,
            new PageData
            {
                FactoryId = DownloadsPageFactory.StaticId,
                Context = new DownloadsPageContext { GameScope = Optional<GameId>.None },
            }
        )
        {
            Text = new StringComponent(Language.DownloadsLeftMenu_AllDownloads),
            Icon = IconValues.Download,
        };

        // Per-game downloads (dynamic)
        this.WhenActivated(disposable =>
        {
            Sdk.Loadouts.Loadout.ObserveAll(connection)
                // An orphaned loadout (its game was uninstalled or moved, or it was created
                // against an install that no longer resolves) has no locatable installation.
                // InstallationInstance below calls ForceGetInstallation, which throws, and the
                // exception escapes the DynamicData pipeline and takes down the whole app --
                // so skip those loadouts here instead of crashing on them.
                .Filter(loadout => loadout.IsVisible() && gameRegistry.TryGetGameInstallation(loadout, out _))
                .Group(loadout => loadout.InstallationId)
                .Transform(group => group.Cache.Items.First().InstallationInstance)
                .Transform(gameInstallation => CreatePerGameDownloadItem(gameInstallation, workspaceController, workspaceId, logger))
                .DisposeMany()
                .OnUI()
                .Bind(out _leftMenuItemsPerGameDownloads)
                .Subscribe()
                .DisposeWith(disposable);
        });
    }

    private static ILeftMenuItemViewModel CreatePerGameDownloadItem(
        GameInstallation gameInstallation,
        IWorkspaceController workspaceController,
        WorkspaceId workspaceId,
        ILogger<DownloadsLeftMenuViewModel> logger)
    {
        var viewModel = new LeftMenuItemViewModel(
            workspaceController,
            workspaceId,
            new PageData
            {
                FactoryId = DownloadsPageFactory.StaticId,
                Context = new DownloadsPageContext { GameScope = gameInstallation.Game.GameId },
            }
        )
        {
            Text = new StringComponent(string.Format(Language.DownloadsLeftMenu_GameSpecificDownloads, gameInstallation.Game.DisplayName)),
            Icon = IconValues.FolderEditOutline, // Initial fallback icon
        };

        // Load game icon asynchronously
        R3.Observable.Return((IGame)gameInstallation.Game)
            .SelectAwait((game, _) => ImageHelper.LoadGameIconAsync(game, (int)ImageSizes.LeftMenuIcon.Width, logger))
            .AsSystemObservable()
            .WhereNotNull()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(bitmap => viewModel.Icon = ImageHelper.CreateIconValueFromBitmap(bitmap, IconValues.FolderEditOutline));

        return viewModel;
    }


}
