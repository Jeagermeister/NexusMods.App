using System.Reactive;
using Humanizer;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public class ActivityFeedItemViewModel : AViewModel<IActivityFeedItemViewModel>, IActivityFeedItemViewModel
{
    public required IconValue Icon { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string TimestampText => Timestamp.Humanize();

    public required ReactiveCommand<Unit, Unit> NavigateCommand { get; init; }
}
