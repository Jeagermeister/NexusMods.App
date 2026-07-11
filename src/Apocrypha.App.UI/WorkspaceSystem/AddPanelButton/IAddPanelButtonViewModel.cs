using System.Reactive;
using Avalonia.Media;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.WorkspaceSystem;

public interface IAddPanelButtonViewModel : IViewModelInterface
{
    public WorkspaceGridState NewLayoutState { get; }

    public IImage ButtonImage { get; }

    public ReactiveCommand<Unit, WorkspaceGridState> AddPanelCommand { get; }
}
