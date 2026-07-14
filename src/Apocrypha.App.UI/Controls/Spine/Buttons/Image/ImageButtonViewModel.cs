using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using Avalonia.Media;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image.LoadoutFlyout;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Image;

public class ImageButtonViewModel : AViewModel<IImageButtonViewModel>, IImageButtonViewModel
{
    [Reactive]
    public bool IsActive { get; set; }

    [Reactive] public string Name { get; set; } = "";

    [Reactive] public IImage Image { get; set; } = Initializers.IImage;

    [Reactive] public ReactiveCommand<Unit,Unit> Click { get; set; } = Initializers.EmptyReactiveCommand;

    public IWorkspaceContext? WorkspaceContext { get; set; }

    public ILoadoutBadgeViewModel? LoadoutBadgeViewModel { get; set; }

    [Reactive] public ReadOnlyObservableCollection<ILoadoutFlyoutItemViewModel>? Loadouts { get; set; }

    [Reactive] public bool HasMultipleLoadouts { get; set; }

    [Reactive] public ReactiveCommand<Unit, Unit> CreateNewLoadoutCommand { get; set; } = Initializers.EmptyReactiveCommand;

    [Reactive] public DateTimeOffset ActivityTimestamp { get; set; }

    public ImageButtonViewModel()
    {
        this.WhenActivated(d =>
        {
            this.WhenAnyValue(vm => vm.IsActive)
                .SubscribeWithErrorLogging(isActive =>
                {
                    if (LoadoutBadgeViewModel != null)
                    {
                        LoadoutBadgeViewModel.IsLoadoutSelected = isActive;
                    }
                })
                .DisposeWith(d);
        });
    }
}
