using System.Reactive;
using Avalonia.Media.Imaging;
using Apocrypha.Sdk.Games;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.GameWidget;

public interface IGameWidgetViewModel : IViewModelInterface
{
    public GameInstallation? Installation { get; set; }
    public string Name { get; }
    public string Version { get; }
    public string Store { get; }
    public IconValue GameStoreIcon { get; }
    public Bitmap Image { get; }
    public ReactiveCommand<Unit, Unit> AddGameCommand { get; set; }
    public ReactiveCommand<Unit, Unit> ViewGameCommand { get; set; }
    public ReactiveCommand<Unit, Unit> RemoveAllLoadoutsCommand { get; set; }
    public IObservable<bool> IsManagedObservable { get; set; }
    public GameWidgetState State { get; set; }
}
