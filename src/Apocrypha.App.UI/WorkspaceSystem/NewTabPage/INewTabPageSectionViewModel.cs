using System.Collections.ObjectModel;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.WorkspaceSystem;

public interface INewTabPageSectionViewModel : IViewModelInterface
{
    public string SectionName { get; }

    public ReadOnlyObservableCollection<INewTabPageSectionItemViewModel> Items { get; }
}
