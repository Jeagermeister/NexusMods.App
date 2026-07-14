using System.Reactive.Disposables;
using System.Reactive.Linq;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.App.UI.Extensions;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public class DashboardGameTileViewModel : AViewModel<IDashboardGameTileViewModel>, IDashboardGameTileViewModel
{
    public IGameWidgetViewModel GameWidget { get; }

    [Reactive] public bool IsSynced { get; private set; } = true;
    [Reactive] public bool NeedsSync { get; private set; }
    [Reactive] public bool IsBusy { get; private set; }

    [Reactive] public DateTimeOffset ActivityTimestamp { get; set; }

    [Reactive] public LoadoutId PrimaryLoadoutId { get; set; }

    public DashboardGameTileViewModel(IGameWidgetViewModel gameWidget, ISynchronizerService syncService)
    {
        GameWidget = gameWidget;

        this.WhenActivated(d =>
        {
            var statusSerialDisposable = new SerialDisposable().DisposeWith(d);

            this.WhenAnyValue(vm => vm.PrimaryLoadoutId)
                // PrimaryLoadoutId defaults to a zero id until ApplyGameTileMembers sets the
                // real one; activation is deferred to the UI thread and can win that race.
                .Where(loadoutId => loadoutId != default)
                .SubscribeWithErrorLogging(loadoutId =>
                {
                    statusSerialDisposable.Disposable = Observable.FromAsync(() => syncService.StatusForLoadout(loadoutId))
                        .Switch()
                        .OnUI()
                        .SubscribeWithErrorLogging(ApplySyncStatus);
                })
                .DisposeWith(d);
        });
    }

    private void ApplySyncStatus(LoadoutSynchronizerState status)
    {
        IsSynced = status is LoadoutSynchronizerState.Current;
        IsBusy = status is LoadoutSynchronizerState.Pending;
        NeedsSync = !IsSynced && !IsBusy;
    }
}
