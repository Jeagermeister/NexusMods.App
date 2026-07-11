using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.Changelog;

public interface IChangelogPageViewModel : IPageViewModelInterface
{
    public Version? TargetVersion { get; set; }

    public ParsedChangelog? ParsedChangelog { get; }

    public int SelectedIndex { get; set; }

    public IMarkdownRendererViewModel MarkdownRendererViewModel { get; }
}
