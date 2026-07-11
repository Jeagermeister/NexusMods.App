using Apocrypha.App.UI.Overlays;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Windows;

public interface IMainWindowViewModel : IViewModelInterface, IWorkspaceWindow
{
    IOverlayViewModel? CurrentOverlay { get; }
}
