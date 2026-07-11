using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Diagnostics;

public class DiagnosticEntryViewModel : AViewModel<IDiagnosticEntryViewModel>, IDiagnosticEntryViewModel
{
    
    public DiagnosticEntryViewModel(Diagnostic diagnostic, IDiagnosticWriter writer)
    {
        Diagnostic = diagnostic;
        Title = diagnostic.Title;
        Summary = diagnostic.FormatSummary(writer);
        Severity = diagnostic.Severity;
        SeeDetailsCommand = ReactiveCommand.Create<NavigationInformation, ValueTuple<Diagnostic, NavigationInformation>>(info => (diagnostic, info));
    }

    public Diagnostic Diagnostic { get; }
    public string Title { get; }
    public string Summary { get; }
    public DiagnosticSeverity Severity { get; }
    public ReactiveCommand<NavigationInformation, ValueTuple<Diagnostic, NavigationInformation>> SeeDetailsCommand { get; }
}
