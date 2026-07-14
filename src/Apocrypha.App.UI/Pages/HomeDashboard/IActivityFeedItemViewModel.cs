using System.Reactive;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public interface IActivityFeedItemViewModel : IViewModelInterface
{
    IconValue Icon { get; }

    string Text { get; }

    DateTimeOffset Timestamp { get; }

    string TimestampText { get; }

    ReactiveCommand<Unit, Unit> NavigateCommand { get; }
}
