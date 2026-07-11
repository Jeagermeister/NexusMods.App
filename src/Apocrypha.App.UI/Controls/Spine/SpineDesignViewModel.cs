using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls.Spine.Buttons.Download;
using Apocrypha.App.UI.Controls.Spine.Buttons.Icon;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image;
using Apocrypha.App.UI.LeftMenu;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Controls.Spine;

public class SpineDesignViewModel : AViewModel<ISpineViewModel>, ISpineViewModel
{
    public ILeftMenuViewModel? LeftMenuViewModel => null;

    public IIconButtonViewModel Home { get; } = new IconButtonDesignViewModel();
    public IIconButtonViewModel AddLoadout { get; } = new IconButtonDesignViewModel();

    public ISpineDownloadButtonViewModel Downloads { get; } = new SpineDownloadButtonDesignerViewModel();

    public ReadOnlyObservableCollection<IImageButtonViewModel> LoadoutSpineItems { get; } =
        new(new ObservableCollection<IImageButtonViewModel>
    {
        new ImageButtonDesignViewModel(),
        new ImageButtonDesignViewModel(),
        new ImageButtonDesignViewModel(),
        new ImageButtonDesignViewModel(),
    });

    public void NavigateToHome() { }
}
