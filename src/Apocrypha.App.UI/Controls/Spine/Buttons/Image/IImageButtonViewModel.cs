using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Media;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image.LoadoutFlyout;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Image;

public interface IImageButtonViewModel : ISpineItemViewModel
{

    /// <summary>
    /// Name for the tooltip on the button
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Image for the button
    /// </summary>
    public IImage Image { get; set; }

    /// <summary>
    /// ViewModel for the loadout badge
    /// </summary>
    public ILoadoutBadgeViewModel? LoadoutBadgeViewModel { get; set; }

    /// <summary>
    /// The game's other loadouts (including the one this button currently opens), shown in the
    /// loadout-switcher flyout. Null/empty when the game has only one loadout.
    /// </summary>
    public ReadOnlyObservableCollection<ILoadoutFlyoutItemViewModel>? Loadouts { get; set; }

    /// <summary>
    /// Whether this game has more than one loadout — drives the chevron/flyout affordance.
    /// </summary>
    public bool HasMultipleLoadouts { get; set; }

    /// <summary>
    /// Creates a new loadout for this game, from the flyout's "+ New loadout" row.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CreateNewLoadoutCommand { get; set; }

    /// <summary>
    /// The game's most-recent activity (its loadouts' last-applied time, falling back to creation
    /// time for never-applied loadouts) — drives spine ordering.
    /// </summary>
    public DateTimeOffset ActivityTimestamp { get; set; }
}
