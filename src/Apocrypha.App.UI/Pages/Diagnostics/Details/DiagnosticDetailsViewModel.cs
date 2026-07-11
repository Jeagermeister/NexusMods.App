using System.Reactive.Disposables;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.Diagnostics;

public class DiagnosticDetailsViewModel : APageViewModel<IDiagnosticDetailsViewModel>, IDiagnosticDetailsViewModel
{
    public DiagnosticSeverity Severity { get; }

    public IMarkdownRendererViewModel MarkdownRendererViewModel { get; }

    public DiagnosticDetailsViewModel(
        IWindowManager windowManager,
        IDiagnosticWriter diagnosticWriter,
        IMarkdownRendererViewModel markdownRendererViewModel,
        Diagnostic diagnostic) : base(windowManager)
    {
        TabIcon = IconValues.Cardiology;
        TabTitle = diagnostic.Title;
        Severity = diagnostic.Severity;

        var summary = diagnostic.FormatSummary(diagnosticWriter);
        var details = $"## {summary}\n" +
                  $"{diagnostic.FormatDetails(diagnosticWriter)}";

        MarkdownRendererViewModel = markdownRendererViewModel;
        MarkdownRendererViewModel.Contents = details;
    }
}
