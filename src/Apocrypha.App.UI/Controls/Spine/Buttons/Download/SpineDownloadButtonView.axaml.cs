using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using Apocrypha.App.UI.Extensions;
using ReactiveUI;
using static Apocrypha.App.UI.Helpers.StyleConstants.SpineDownloadButton;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Download;

public partial class SpineDownloadButtonView : ReactiveUserControl<ISpineDownloadButtonViewModel>
{
    public SpineDownloadButtonView()
    {
        InitializeComponent();

        this.WhenActivated(d =>
        {
            this.WhenAnyValue(view => view.ViewModel!.Click)
                .Select(System.Windows.Input.ICommand? (command) => command)
                .OnUI()
                .BindToView(this, view => view.ParentButton.Command)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Progress)
                .Select(p => p.HasValue)
                .BindToClasses(ParentButton, Progress, Idle)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.IsActive)
                .BindToActive(ParentButton)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Progress)
                .Where(p => p.HasValue)
                .Select(p => p.Value.Value * 360)
                .OnUI()
                .BindToView(this, view => view.ProgressArc.SweepAngle)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Number)
                .Select(n => n.ToString("###0.0"))
                .OnUI()
                .BindToView(this, view => view.NumberTextBlock.Text)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Units)
                .Select(n => n.ToUpperInvariant())
                .OnUI()
                .BindToView(this, view => view.UnitsTextBlock.Text)
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.ToolTip,
                    v => v.ToolTipTextBlock.Text)
                .DisposeWith(d);
        });
    }
}
