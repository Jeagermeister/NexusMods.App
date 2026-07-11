using Apocrypha.App.UI.LeftMenu;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.WorkspaceAttachments;

public interface IWorkspaceAttachmentsFactoryManager
{
    public ILeftMenuViewModel? CreateLeftMenuFor(IWorkspaceContext context, WorkspaceId workspaceId, IWorkspaceController workspaceController);

    public string CreateTitleFor(IWorkspaceContext context);
    
    public string CreateSubtitleFor(IWorkspaceContext context);
}
