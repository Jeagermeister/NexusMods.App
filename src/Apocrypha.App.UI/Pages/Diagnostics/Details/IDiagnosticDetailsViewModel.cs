using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.Diagnostics;

public interface IDiagnosticDetailsViewModel : IPageViewModelInterface
{
    DiagnosticSeverity Severity { get; }

    IMarkdownRendererViewModel MarkdownRendererViewModel { get; }
}
