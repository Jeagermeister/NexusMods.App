using System.Reactive;
using Apocrypha.App.UI;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.Games.FOMOD.UI;

public class GuidedInstallerWindowDesignViewModel : AViewModel<IGuidedInstallerWindowViewModel>, IGuidedInstallerWindowViewModel
{
    public string WindowName { get; set; } = "Test FOMOD Installer";

    public IGuidedInstallerStepViewModel? ActiveStepViewModel { get; set; }

    public ReactiveCommand<Unit, Unit> CloseCommand => Initializers.EnabledReactiveCommand;
}
