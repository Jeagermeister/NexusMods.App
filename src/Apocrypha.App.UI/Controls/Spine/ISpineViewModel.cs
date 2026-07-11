using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls.Spine.Buttons.Download;
using Apocrypha.App.UI.Controls.Spine.Buttons.Icon;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image;
using Apocrypha.App.UI.LeftMenu;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Controls.Spine;

public interface ISpineViewModel : IViewModelInterface
{
    /// <summary>
    /// Gets the left menu view model.
    /// </summary>
    public ILeftMenuViewModel? LeftMenuViewModel { get; }

    /// <summary>
    /// Gets the home button.
    /// </summary>
    public IIconButtonViewModel Home { get; }
    
    /// <summary>
    /// Gets the add loadout button.
    /// </summary>
    public IIconButtonViewModel AddLoadout { get; }

    /// <summary>
    /// Gets the downloads button.
    /// </summary>
    public ISpineDownloadButtonViewModel Downloads { get; }

    /// <summary>
    /// Gets all loadout buttons.
    /// </summary>
    public ReadOnlyObservableCollection<IImageButtonViewModel> LoadoutSpineItems { get; }

    public void NavigateToHome();
}
