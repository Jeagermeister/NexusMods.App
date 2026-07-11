using ReactiveUI;
using System.Reactive;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Controls.MiniGameWidget.ComingSoon;

public interface IComingSoonMiniGameWidgetViewModel : IViewModelInterface
{
    ReactiveCommand<Unit, Unit> ViewRoadmapCommand { get; }
}
