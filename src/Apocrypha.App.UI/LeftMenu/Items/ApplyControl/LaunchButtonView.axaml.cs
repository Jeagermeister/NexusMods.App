using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace Apocrypha.App.UI.LeftMenu.Items;

public partial class LaunchButtonView : ReactiveUserControl<ILaunchButtonViewModel>
{
    public LaunchButtonView()
    {
        InitializeComponent();
        
        this.WhenActivated(d =>
        {
            var isRunning = ViewModel!.IsRunningObservable;
            var isNotRunning = ViewModel!.IsRunningObservable.Select(running => !running);

            isNotRunning.OnUI().BindToView(this, view => view.LaunchButton.IsEnabled)
                .DisposeWith(d);
            
            // Show progress bar when running
            isRunning.OnUI().BindToView(this, view => view.LaunchSpinner.IsVisible)
                .DisposeWith(d);
            
            // Show icon when not running
            isNotRunning.OnUI().BindToView(this, view => view.LaunchIcon.IsVisible)
                .DisposeWith(d);
            
            // Bind the 'launch' button.
            this.WhenAnyValue(view => view.ViewModel!.Command)
                .Select(System.Windows.Input.ICommand? (command) => command)
                .OnUI()
                .BindToView(this, view => view.LaunchButton.Command)
                .DisposeWith(d);
            
            // Set the 'play' / 'running' text.
            this.WhenAnyValue(view => view.ViewModel!.Label)
                .Select(string? (label) => label)
                .OnUI()
                .BindToView(this, view => view.LaunchText.Text)
                .DisposeWith(d);
        });
    }
}

