using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls.LoadoutCard;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Pages.MyLoadouts.GameLoadoutsSectionEntry;

public interface IGameLoadoutsSectionEntryViewModel : IViewModelInterface, IDisposable
{
    string HeadingText { get; }
    ReadOnlyObservableCollection<IViewModelInterface> CardViewModels { get; }
}
