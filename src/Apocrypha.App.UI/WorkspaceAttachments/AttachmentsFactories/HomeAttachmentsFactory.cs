using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.WorkspaceAttachments;

public class HomeAttachmentsFactory : IWorkspaceAttachmentsFactory<HomeContext>
{
    public string CreateTitle(HomeContext context)
    {
        return Language.HomeWorkspace_Title;
    }

    public string CreateSubtitle(HomeContext context)
    {
        return string.Empty;
    }
}
