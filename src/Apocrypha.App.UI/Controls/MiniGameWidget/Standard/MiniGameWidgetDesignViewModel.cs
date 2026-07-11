using System.Reactive;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Apocrypha.Abstractions.Games;
using Apocrypha.Sdk.Games;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.Controls.MiniGameWidget.Standard;

public class MiniGameWidgetDesignViewModel : AViewModel<IMiniGameWidgetViewModel>, IMiniGameWidgetViewModel
{
    public static MiniGameWidgetDesignViewModel Instance { get; } = new();

    [Reactive] public IGame? Game { get; set; }
    public GameInstallation[]? GameInstallations { get; set; }
    public string Name { get; set; } = "Cyberpunk 2077";
    public bool IsFound { get; set; } = true;
    public Bitmap Image { get; set; } = new(AssetLoader.Open(new Uri("avares://Apocrypha.App.UI/Assets/DesignTime/cyberpunk_game.png")));

    public ReactiveCommand<Unit, Unit> GiveFeedbackCommand { get; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
}
