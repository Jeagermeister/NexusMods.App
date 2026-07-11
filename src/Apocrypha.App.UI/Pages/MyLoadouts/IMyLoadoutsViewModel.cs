using System.Collections.ObjectModel;
using Apocrypha.App.UI.Pages.MyLoadouts.GameLoadoutsSectionEntry;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.MyLoadouts;


public interface IMyLoadoutsViewModel : IPageViewModelInterface
{
    ReadOnlyObservableCollection<IGameLoadoutsSectionEntryViewModel> GameSectionViewModels { get; }
}
