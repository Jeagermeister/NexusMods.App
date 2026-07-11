using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.Diagnostics;

public class DiagnosticDetailsDesignViewModel : APageViewModel<IDiagnosticDetailsViewModel>, IDiagnosticDetailsViewModel
{
    private const string Details = "This is an example diagnostic details, lots of stuff here.";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Critical;

    public IMarkdownRendererViewModel MarkdownRendererViewModel => new MarkdownRendererViewModel
    {
        Contents = Details
    };

    public DiagnosticDetailsDesignViewModel() : base(new DesignWindowManager()) { }

}
