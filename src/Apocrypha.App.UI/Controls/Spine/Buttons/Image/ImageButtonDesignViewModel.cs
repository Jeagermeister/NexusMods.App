using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Image;

public class ImageButtonDesignViewModel : ImageButtonViewModel
{
    public ImageButtonDesignViewModel()
    {
        Image = new Bitmap(AssetLoader.Open(new Uri("avares://Apocrypha.App.UI/Assets/DesignTime/cyberpunk_game.png")));
        Click = ReactiveCommand.Create(() => { IsActive = !IsActive; });
        Name = "Image Text";
        LoadoutBadgeViewModel = new LoadoutBadgeDesignViewModel();
    }
}
