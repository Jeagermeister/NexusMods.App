using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.WorkspaceSystem;

public interface INewTabPageSectionItemViewModel : IViewModelInterface
{
    public string SectionName { get; }

    public string Name { get; }

    public IconValue Icon { get; }

    public ReactiveCommand<NavigationInformation, ValueTuple<PageData, NavigationInformation>> SelectItemCommand { get; }
}
