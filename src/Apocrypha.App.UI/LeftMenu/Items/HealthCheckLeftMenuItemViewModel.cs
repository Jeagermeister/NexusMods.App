using System.Reactive.Disposables;
using System.Reactive.Linq;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Settings;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.Sdk.Loadouts;
using R3;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class HealthCheckLeftMenuItemViewModel : LeftMenuItemViewModel
{
    [Reactive] public bool IsSuggestionVisible { get; private set; }
    [Reactive] public bool IsWarningVisible { get; private set; }
    [Reactive] public bool IsCriticalVisible { get; private set; }

    public HealthCheckLeftMenuItemViewModel(
        IWorkspaceController workspaceController,
        ISettingsManager settingsManager,
        WorkspaceId workspaceId,
        PageData pageData,
        IDiagnosticManager diagnosticManager,
        LoadoutId loadoutId) : base(workspaceController, workspaceId, pageData)
    {
        var healthCheckCountsObservable = diagnosticManager
            .CountDiagnostics(loadoutId)
            .CombineLatest(settingsManager.GetChanges<DiagnosticSettings>(prependCurrent: true).AsSystemObservable())
            .OnUI()
            .Do(update =>
                {
                    var (counts, settings) = update;
                    IsCriticalVisible = counts.NumCritical != 0 && settings.MinimumSeverity <= DiagnosticSeverity.Critical;
                    IsWarningVisible = counts.NumWarnings != 0 && !IsCriticalVisible && settings.MinimumSeverity <= DiagnosticSeverity.Warning;
                    IsSuggestionVisible = counts.NumSuggestions != 0 && !IsCriticalVisible && !IsWarningVisible && settings.MinimumSeverity <= DiagnosticSeverity.Suggestion;
                }
            );

        this.WhenActivated(d =>
            {
                healthCheckCountsObservable.Subscribe()
                    .DisposeWith(d);
            }
        );
    }
}
