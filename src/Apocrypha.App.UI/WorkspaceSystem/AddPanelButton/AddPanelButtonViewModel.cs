using System.Reactive;
using Avalonia.Media;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.WorkspaceSystem;

public class AddPanelButtonViewModel : AViewModel<IAddPanelButtonViewModel>, IAddPanelButtonViewModel
{
    public WorkspaceGridState NewLayoutState { get; }
    public IImage ButtonImage { get; }
    public ReactiveCommand<Unit, WorkspaceGridState> AddPanelCommand { get; }

    public AddPanelButtonViewModel(
        WorkspaceGridState newLayoutState,
        IImage buttonImage)
    {
        NewLayoutState = newLayoutState;
        ButtonImage = buttonImage;

        AddPanelCommand = ReactiveCommand.Create(() => NewLayoutState);
    }
}
