using System.Collections.ObjectModel;
using System.Reactive;
using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.App.UI.Controls.MiniGameWidget;
using Apocrypha.App.UI.Controls.MiniGameWidget.Standard;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.MyGames;

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
