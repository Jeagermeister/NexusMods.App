using Apocrypha.App.UI.Dialog.Enums;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Dialog;

namespace Apocrypha.App.UI.Dialog;

public interface IDialogViewModel : IViewModelInterface
{
    public R3.ReactiveCommand<ButtonDefinitionId, ButtonDefinitionId> ButtonPressCommand { get; }
    public string WindowTitle { get; }
    public DialogWindowSize DialogWindowSize { get; }
    public IViewModelInterface? ContentViewModel { get; }
    public DialogButtonDefinition[] ButtonDefinitions { get; }
    public bool ShowChrome { get; set; }
    public StandardDialogResult Result { get; set; }
}
