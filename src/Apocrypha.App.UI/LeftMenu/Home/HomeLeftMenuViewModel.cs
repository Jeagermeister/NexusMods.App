using System.Collections.ObjectModel;
using JetBrains.Annotations;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.LeftMenu.Items;
using Apocrypha.App.UI.Pages.MyGames;
using Apocrypha.App.UI.Pages.MyLoadouts;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.LeftMenu.Home;

[UsedImplicitly]
public class HomeLeftMenuViewModel : AViewModel<IHomeLeftMenuViewModel>, IHomeLeftMenuViewModel
{
    public WorkspaceId WorkspaceId { get; }
    public ILeftMenuItemViewModel LeftMenuItemMyGames { get; }
    public ILeftMenuItemViewModel LeftMenuItemMyLoadouts { get; }

    public HomeLeftMenuViewModel(
        IMyGamesViewModel myGamesViewModel,
        WorkspaceId workspaceId,
        IWorkspaceController workspaceController)
    {
        WorkspaceId = workspaceId;

        LeftMenuItemMyGames = new LeftMenuItemViewModel(
            workspaceController,
            WorkspaceId,
            new PageData
            {
                FactoryId = MyGamesPageFactory.StaticId,
                Context = new MyGamesPageContext(),
            }
        )
        {
            Text = new StringComponent(Language.MyGames),
            Icon = IconValues.GamepadOutline,
        };
        
        LeftMenuItemMyLoadouts = new LeftMenuItemViewModel(
            workspaceController,
            WorkspaceId,
            new PageData
            {
                FactoryId = MyLoadoutsPageFactory.StaticId,
                Context = new MyLoadoutsPageContext(),
            }
        )
        {
            Text = new StringComponent(Language.MyLoadoutsPageTitle),
            Icon = IconValues.Package,
        };
    }
}
