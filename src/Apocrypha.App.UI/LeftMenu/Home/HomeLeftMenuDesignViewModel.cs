using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.LeftMenu.Items;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.LeftMenu.Home;

public class HomeLeftMenuDesignViewModel : AViewModel<IHomeLeftMenuViewModel>, IHomeLeftMenuViewModel
{
    public WorkspaceId WorkspaceId { get; } = WorkspaceId.NewId();
    
    public ILeftMenuItemViewModel LeftMenuItemMyGames { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.MyGames),
        Icon = IconValues.GamepadOutline,
    };
    public ILeftMenuItemViewModel LeftMenuItemMyLoadouts { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.MyLoadoutsPageTitle),
        Icon = IconValues.Package,
    };
    
}
