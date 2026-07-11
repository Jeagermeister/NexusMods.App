using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.App.UI.Controls.MiniGameWidget.ComingSoon;
using Apocrypha.App.UI.Controls.MiniGameWidget.Standard;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.MyGames;

public class MyGamesDesignViewModel : APageViewModel<IMyGamesViewModel>, IMyGamesViewModel
{
    public ReactiveCommand<Unit, Unit> GiveFeedbackCommand => Initializers.DisabledReactiveCommand;
    public ReactiveCommand<Unit, Unit> OpenRoadmapCommand => Initializers.DisabledReactiveCommand;
    public ReadOnlyObservableCollection<IGameWidgetViewModel> InstalledGames { get; }
    public ReadOnlyObservableCollection<IViewModelInterface> SupportedGames { get; }
    public string SupportedGamesSearchText { get; set; } = string.Empty;

    public MyGamesDesignViewModel() : base(new DesignWindowManager())
    {
        var detectedGames = Enumerable.Range(0, 2)
            .Select(_ => new GameWidgetDesignViewModel())
            .ToArray();

        var supportedGames = Enumerable.Range(0, 3)
            .Select(_ => new MiniGameWidgetDesignViewModel())
            .ToList();

        InstalledGames = new ReadOnlyObservableCollection<IGameWidgetViewModel>(new ObservableCollection<IGameWidgetViewModel>(detectedGames));

        SupportedGames = new ReadOnlyObservableCollection<IViewModelInterface>(
            [
                new MiniGameWidgetDesignViewModel(),
                new MiniGameWidgetDesignViewModel(),
                new ComingSoonMiniGameWidgetViewModelDesignViewModel(),
            ]
        );
    }
}
