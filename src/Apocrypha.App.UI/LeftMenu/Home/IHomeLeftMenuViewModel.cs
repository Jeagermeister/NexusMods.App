using Apocrypha.App.UI.LeftMenu.Items;

namespace Apocrypha.App.UI.LeftMenu.Home;

public interface IHomeLeftMenuViewModel : ILeftMenuViewModel
{
    public ILeftMenuItemViewModel LeftMenuItemMyGames { get; }
    
    public ILeftMenuItemViewModel LeftMenuItemMyLoadouts { get; }
    
}
