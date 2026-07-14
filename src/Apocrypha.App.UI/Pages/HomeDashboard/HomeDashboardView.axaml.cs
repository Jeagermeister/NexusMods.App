using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

public partial class HomeDashboardView : ReactiveUserControl<IHomeDashboardViewModel>
{
    public HomeDashboardView()
    {
        InitializeComponent();

        this.WhenActivated(d =>
        {
            this.WhenAnyValue(view => view.ViewModel!.GameTiles)
                .BindToView(this, view => view.GameTilesItemsControl.ItemsSource)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.ActivityFeed)
                .BindToView(this, view => view.ActivityFeedItemsControl.ItemsSource)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.ActivityFeed.Count)
                .Select(count => count == 0)
                .Subscribe(isEmpty => NoActivityTextBlock.IsVisible = isEmpty)
                .DisposeWith(d);
        });
    }
}
