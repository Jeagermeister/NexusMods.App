using Microsoft.Extensions.DependencyInjection;
using Apocrypha.App.UI.Pages.MyGames;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.LeftMenu.Home;

public class HomeLeftMenuFactory(IServiceProvider serviceProvider) : ILeftMenuFactory<HomeContext>
{
    public ILeftMenuViewModel CreateLeftMenuViewModel(HomeContext context, WorkspaceId workspaceId,
        IWorkspaceController workspaceController)
    {
        return new HomeLeftMenuViewModel(serviceProvider.GetRequiredService<IMyGamesViewModel>(), workspaceId,
            workspaceController);
    }
}
