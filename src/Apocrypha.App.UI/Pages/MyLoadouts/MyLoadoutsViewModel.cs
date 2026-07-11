using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.Pages.MyLoadouts.GameLoadoutsSectionEntry;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.MyLoadouts;

public class MyLoadoutsViewModel : APageViewModel<IMyLoadoutsViewModel>, IMyLoadoutsViewModel
{
    private ReadOnlyObservableCollection<IGameLoadoutsSectionEntryViewModel> _gameSectionViewModels = new([]);

    public ReadOnlyObservableCollection<IGameLoadoutsSectionEntryViewModel> GameSectionViewModels => _gameSectionViewModels;

    public MyLoadoutsViewModel(
        IWindowManager windowManager,
        IConnection conn,
        IServiceProvider serviceProvider) : base(windowManager)
    {
        TabTitle = Language.MyLoadoutsPageTitle;
        TabIcon = IconValues.Package;
        
        this.WhenActivated(d =>
        {
            Loadout.ObserveAll(conn)
                // Orphaned loadouts (game uninstalled/moved) can't resolve an installation
                .Filter(l => l.IsVisible() && serviceProvider.GetRequiredService<Apocrypha.Sdk.Games.IGameRegistry>().TryGetGameInstallation(l, out _))
                .DistinctValues(loadout => loadout.InstallationInstance)
                .Transform(managedGameInstall =>
                    {
                        return (IGameLoadoutsSectionEntryViewModel)new GameLoadoutsSectionEntryViewModel(
                            managedGameInstall,
                            conn,
                            serviceProvider,
                            windowManager
                        );
                    }
                )
                .OnUI()
                .Bind(out _gameSectionViewModels)
                // These entries are not used as actual vms, just as data source for DataTemplates in a ItemsControl,
                // so they need manual disposal as there is no WhenActivated mechanism for them
                .DisposeMany()
                .SubscribeWithErrorLogging()
                .DisposeWith(d);
        });
    }

}
