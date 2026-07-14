using System.Collections.ObjectModel;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public class HomeDashboardDesignViewModel : APageViewModel<IHomeDashboardViewModel>, IHomeDashboardViewModel
{
    public ReadOnlyObservableCollection<IDashboardGameTileViewModel> GameTiles { get; }
    public ReadOnlyObservableCollection<IActivityFeedItemViewModel> ActivityFeed { get; }

    public HomeDashboardDesignViewModel() : base(new DesignWindowManager())
    {
        GameTiles = new ReadOnlyObservableCollection<IDashboardGameTileViewModel>(new ObservableCollection<IDashboardGameTileViewModel>([]));
        ActivityFeed = new ReadOnlyObservableCollection<IActivityFeedItemViewModel>(new ObservableCollection<IActivityFeedItemViewModel>([]));
    }
}
