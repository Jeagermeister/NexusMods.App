using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.UI.Sdk;
using R3;

namespace Apocrypha.App.UI.Pages.Sorting;

public interface ISortingSelectionViewModel : IViewModelInterface
{
    ReadOnlyObservableCollection<IViewModelInterface> RulesViewModels { get; }

    public IReadOnlyBindableReactiveProperty<bool> CanEdit { get; }
    
    public ReactiveCommand<NavigationInformation> OpenAllModsLoadoutPageCommand { get; }
}
