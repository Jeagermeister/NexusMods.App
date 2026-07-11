using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.LoadoutGroupFilesPage;

public interface ILoadoutGroupFilesViewModel : IPageViewModelInterface
{
    LoadoutGroupFilesPageContext? Context { get; set; }

    R3.ReactiveCommand<NavigationInformation> OpenEditorCommand { get; }

    R3.ReactiveCommand<R3.Unit> RemoveCommand { get; }
    
    ViewLoadoutGroupFilesTreeDataGridAdapter? FileTreeAdapter { get; set; }
}
