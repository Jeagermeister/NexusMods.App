using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class LeftMenuItemWithRightIconViewModel : LeftMenuItemViewModel
{
    public required IconValue RightIcon { get; init; }

    public LeftMenuItemWithRightIconViewModel(
        IWorkspaceController workspaceController,
        WorkspaceId workspaceId,
        PageData pageData) : base(workspaceController, workspaceId, pageData) { }
}
