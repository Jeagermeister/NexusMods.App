using System.Reactive;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Image.LoadoutFlyout;

/// <summary>
/// One row in a spine game button's loadout-switcher flyout.
/// </summary>
public interface ILoadoutFlyoutItemViewModel : IViewModelInterface
{
    LoadoutId LoadoutId { get; }

    string Name { get; }

    ILoadoutBadgeViewModel LoadoutBadgeViewModel { get; }

    ReactiveCommand<Unit, Unit> VisitLoadoutCommand { get; }
}
