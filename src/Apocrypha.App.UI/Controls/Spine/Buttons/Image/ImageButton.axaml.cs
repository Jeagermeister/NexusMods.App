using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Image;

public partial class ImageButton : ReactiveUserControl<IImageButtonViewModel>
{
    public ImageButton()
    {
        InitializeComponent();

        this.WhenActivated(d =>
        {
            this.WhenAnyValue(vm => vm.ViewModel!.IsActive)
                .StartWith(false)
                .SubscribeWithErrorLogging(SetClasses)
                .DisposeWith(d);

            this.BindCommand(ViewModel, vm => vm.Click, v => v.Button)
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.Image, view => view.Image.Value, image => new IconValue(new AvaloniaImage(image)))
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.Name, v => v.ToolTipTextBlock.Text)
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.LoadoutBadgeViewModel, v => v.LoadoutBadge.ViewModel)
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.HasMultipleLoadouts, v => v.LoadoutFlyoutChevron.IsVisible)
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.Loadouts, v => v.LoadoutFlyoutItemsControl.ItemsSource)
                .DisposeWith(d);

            this.BindCommand(ViewModel, vm => vm.CreateNewLoadoutCommand, v => v.CreateNewLoadoutButton)
                .DisposeWith(d);

            // Clicking a loadout row or "New loadout" should close the popup, not just navigate.
            LoadoutFlyoutContentStack.AddHandler(Button.ClickEvent, OnLoadoutFlyoutContentClick, RoutingStrategies.Bubble);
            Disposable.Create(() => LoadoutFlyoutContentStack.RemoveHandler(Button.ClickEvent, OnLoadoutFlyoutContentClick))
                .DisposeWith(d);
        });
    }

    private void OnLoadoutFlyoutContentClick(object? sender, RoutedEventArgs e)
    {
        LoadoutFlyoutChevron.Flyout?.Hide();
    }

    private void SetClasses(bool isActive)
    {
        if (isActive)
        {
            Button.Classes.Add("Active");
            Button.Classes.Remove("Inactive");
        }
        else
        {
            Button.Classes.Remove("Active");
            Button.Classes.Add("Inactive");
        }
    }

}
