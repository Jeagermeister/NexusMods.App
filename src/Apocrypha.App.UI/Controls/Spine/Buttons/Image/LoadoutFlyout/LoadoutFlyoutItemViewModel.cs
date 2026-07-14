using System.Reactive;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Image.LoadoutFlyout;

public class LoadoutFlyoutItemViewModel : AViewModel<ILoadoutFlyoutItemViewModel>, ILoadoutFlyoutItemViewModel
{
    public LoadoutId LoadoutId { get; }

    public string Name { get; }

    public ILoadoutBadgeViewModel LoadoutBadgeViewModel { get; }

    public required ReactiveCommand<Unit, Unit> VisitLoadoutCommand { get; init; }

    public LoadoutFlyoutItemViewModel(Loadout.ReadOnly loadout, IServiceProvider serviceProvider)
    {
        LoadoutId = loadout.LoadoutId;
        Name = loadout.Name;

        var badgeViewModel = serviceProvider.GetRequiredService<ILoadoutBadgeViewModel>();
        badgeViewModel.LoadoutValue = loadout;
        LoadoutBadgeViewModel = badgeViewModel;
    }
}
