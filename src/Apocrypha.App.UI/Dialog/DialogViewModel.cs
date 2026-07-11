using System.ComponentModel;
using Apocrypha.App.UI.Dialog.Enums;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Dialog;
using ReactiveUI;

namespace Apocrypha.App.UI.Dialog;

public class DialogViewModel: IDialogViewModel
{
#pragma warning disable CS0067 // required by INotifyPropertyChanged, never raised here
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
    public ViewModelActivator Activator { get; }
    public R3.ReactiveCommand<ButtonDefinitionId, ButtonDefinitionId> ButtonPressCommand { get; }
    public string WindowTitle { get; }
    public DialogWindowSize DialogWindowSize { get; }
    public IViewModelInterface? ContentViewModel { get; }
    public DialogButtonDefinition[] ButtonDefinitions { get; }
    public bool ShowChrome { get; set; }
    public StandardDialogResult Result { get; set; }

    public DialogViewModel(string title, DialogButtonDefinition[] buttonsDefinitions, IViewModelInterface contentViewModel, DialogWindowSize dialogWindowSize, bool showChrome) {
        Activator = new ViewModelActivator();
        WindowTitle = title;
        DialogWindowSize = dialogWindowSize;
        ContentViewModel = contentViewModel;
        ButtonDefinitions = buttonsDefinitions;
        ShowChrome = showChrome;
        Result = new StandardDialogResult();
        
        ButtonPressCommand = new R3.ReactiveCommand<ButtonDefinitionId, ButtonDefinitionId>(id =>
            {
                Console.WriteLine(id);
                Result = new StandardDialogResult
                {
                    ButtonId = id,
                    InputText = contentViewModel is IDialogStandardContentViewModel standardContentViewModel
                        ? standardContentViewModel.InputText
                        : string.Empty
                };
                return id;
            }
        );
    }
    
}
