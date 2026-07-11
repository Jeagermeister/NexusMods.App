using System.Reactive;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.WorkspaceSystem;

public interface IPanelTabHeaderViewModel : IViewModelInterface
{
    public PanelTabId Id { get; }

    public string Title { get; set; }

    public IconValue Icon { get; set; }

    public bool IsSelected { get; set;  }

    public bool CanClose { get; set; }

    public ReactiveCommand<Unit, PanelTabId> CloseTabCommand { get; }
}
