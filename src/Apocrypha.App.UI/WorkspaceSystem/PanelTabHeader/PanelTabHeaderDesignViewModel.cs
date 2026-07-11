using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.WorkspaceSystem;

public class PanelTabHeaderDesignViewModel : PanelTabHeaderViewModel
{
    public PanelTabHeaderDesignViewModel() : base(PanelTabId.DefaultValue)
    {
        Title = "Very long name for tab headers";

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Apocrypha.App.UI/Assets/DesignTime/cyberpunk_game.png"));
            Icon = new AvaloniaImage(new Bitmap(stream));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
