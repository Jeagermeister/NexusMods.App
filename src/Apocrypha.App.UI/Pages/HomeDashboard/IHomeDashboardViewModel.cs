using System.Collections.ObjectModel;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public interface IHomeDashboardViewModel : IPageViewModelInterface
{
    /// <summary>
    /// One tile per managed game ("jump back in"), ordered by most recent activity.
    /// </summary>
    ReadOnlyObservableCollection<IDashboardGameTileViewModel> GameTiles { get; }

    /// <summary>
    /// Recent installs and loadout applies across every managed game, newest first.
    /// </summary>
    ReadOnlyObservableCollection<IActivityFeedItemViewModel> ActivityFeed { get; }
}
