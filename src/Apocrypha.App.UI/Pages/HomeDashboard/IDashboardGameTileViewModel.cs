using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public interface IDashboardGameTileViewModel : IViewModelInterface
{
    IGameWidgetViewModel GameWidget { get; }

    /// <summary>The active loadout is applied and up to date.</summary>
    bool IsSynced { get; }

    /// <summary>The active loadout has unapplied changes, or isn't the one currently applied.</summary>
    bool NeedsSync { get; }

    /// <summary>An apply/sync is in progress.</summary>
    bool IsBusy { get; }

    /// <summary>The game's most recent activity — drives dashboard tile ordering.</summary>
    DateTimeOffset ActivityTimestamp { get; set; }

    /// <summary>The loadout this tile currently represents (its most recently active one).</summary>
    LoadoutId PrimaryLoadoutId { get; set; }
}
