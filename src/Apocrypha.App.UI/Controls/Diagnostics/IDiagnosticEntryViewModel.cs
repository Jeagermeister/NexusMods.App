using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Diagnostics;

public interface IDiagnosticEntryViewModel : IViewModelInterface
{
    Diagnostic Diagnostic { get; }

    string Title { get; }
    
    string Summary { get; }
    
    DiagnosticSeverity Severity { get; }
    
    ReactiveCommand<NavigationInformation, ValueTuple<Diagnostic, NavigationInformation>> SeeDetailsCommand { get; }
}

