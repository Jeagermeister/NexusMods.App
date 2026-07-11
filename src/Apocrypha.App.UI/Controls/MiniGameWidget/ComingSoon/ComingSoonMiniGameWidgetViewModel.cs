using System.Reactive;
using Microsoft.Extensions.Logging;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Settings;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.MiniGameWidget.ComingSoon;

public class ComingSoonMiniGameWidgetViewModel : AViewModel<IComingSoonMiniGameWidgetViewModel>, IComingSoonMiniGameWidgetViewModel
{
    private readonly ILogger<ComingSoonMiniGameWidgetViewModel> _logger;
    private static readonly Uri ViewRoadmapUri = new("https://trello.com/b/gPzMuIr3/nexus-mods-app-roadmap");

    public ComingSoonMiniGameWidgetViewModel(ILogger<ComingSoonMiniGameWidgetViewModel> logger, 
        IOSInterop osInterop,
        ISettingsManager settingsManager)
    {
        _logger = logger;

        ViewRoadmapCommand = ReactiveCommand.Create(() => osInterop.OpenUri(ViewRoadmapUri));
    }

    public ReactiveCommand<Unit, Unit> ViewRoadmapCommand { get; }

}
