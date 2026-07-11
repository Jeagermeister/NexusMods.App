using System.Collections.ObjectModel;
using System.Reactive;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Ids;
using Apocrypha.App.UI.Controls.Diagnostics;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.Sdk.Loadouts;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.Diagnostics;

public interface IDiagnosticListViewModel : IPageViewModelInterface
{
    public LoadoutId LoadoutId { get; set; }

    public IDiagnosticEntryViewModel[] DiagnosticEntries { get; }

    public int NumCritical { get; }
    public int NumWarnings { get; }
    public int NumSuggestions { get; }

    public DiagnosticFilter Filter { get; set; }


}

[Flags]
public enum DiagnosticFilter
{
    None = 0,
    Critical = 1 << 0,
    Warnings = 1 << 1,
    Suggestions = 1 << 2,
}
