using System.Collections.ObjectModel;
using System.Reactive;
using NexusMods.App.UI.Controls.GameWidget;
using NexusMods.App.UI.Controls.MiniGameWidget;
using NexusMods.App.UI.Controls.MiniGameWidget.Standard;
using NexusMods.App.UI.WorkspaceSystem;
using NexusMods.UI.Sdk;
using ReactiveUI;

namespace NexusMods.App.UI.Pages.MyGames;

public interface IMyGamesViewModel : IPageViewModelInterface
{
    public ReactiveCommand<Unit, Unit> OpenRoadmapCommand { get; }

    public ReadOnlyObservableCollection<IGameWidgetViewModel> InstalledGames { get; }

    public ReadOnlyObservableCollection<IViewModelInterface> SupportedGames { get; }

    /// <summary>
    /// Filters the "Other supported games" section by display name (~200 games since the
    /// BepInEx family); empty shows all.
    /// </summary>
    public string SupportedGamesSearchText { get; set; }
}
