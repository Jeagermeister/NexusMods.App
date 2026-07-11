using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.WorkspaceAttachments;

public class DownloadsAttachmentsFactory : IWorkspaceAttachmentsFactory<DownloadsContext>
{
    public string CreateTitle(DownloadsContext context)
    {
        return Language.Downloads_WorkspaceTitle;
    }

    public string CreateSubtitle(DownloadsContext context)
    {
        return string.Empty;
    }
}
