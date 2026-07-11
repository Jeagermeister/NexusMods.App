using System.Reactive;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.Games.FOMOD.UI;

public interface IGuidedInstallerWindowViewModel : IViewModelInterface
{
    public string WindowName { get; set; }

    public IGuidedInstallerStepViewModel? ActiveStepViewModel { get; set; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
}
