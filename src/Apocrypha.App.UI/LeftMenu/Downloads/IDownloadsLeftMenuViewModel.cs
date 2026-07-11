using System.Collections.ObjectModel;
using Apocrypha.App.UI.LeftMenu.Items;

namespace Apocrypha.App.UI.LeftMenu.Downloads;

public interface IDownloadsLeftMenuViewModel : ILeftMenuViewModel
{
    public ILeftMenuItemViewModel LeftMenuItemAllDownloads { get; }
    
    public ReadOnlyObservableCollection<ILeftMenuItemViewModel> LeftMenuItemsPerGameDownloads { get; }
}