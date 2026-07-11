using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.LeftMenu.Loadout;

public class LoadoutLeftMenuFactory(IServiceProvider serviceProvider) : ILeftMenuFactory<LoadoutContext>
{
    public ILeftMenuViewModel CreateLeftMenuViewModel(LoadoutContext context, WorkspaceId workspaceId,
        IWorkspaceController workspaceController)
    {
        return new LoadoutLeftMenuViewModel(context, workspaceId, workspaceController, serviceProvider);
    }
}
