using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls.Alerts;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.WorkspaceSystem;

public interface INewTabPageViewModel : IPageViewModelInterface
{
    ReadOnlyObservableCollection<INewTabPageSectionViewModel> Sections { get; }

    AlertSettingsWrapper AlertSettingsWrapper { get; }
}
