using System.Collections.ObjectModel;
using Apocrypha.App.UI.Pages.MyLoadouts.GameLoadoutsSectionEntry;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.MyLoadouts;

public class MyLoadoutsDesignViewModel : APageViewModel<IMyLoadoutsViewModel>, IMyLoadoutsViewModel
{

    public MyLoadoutsDesignViewModel() : base(new DesignWindowManager())
    {
    }

    public MyLoadoutsDesignViewModel(IWindowManager windowManager) : base(windowManager)
    {
    }

    public ReadOnlyObservableCollection<IGameLoadoutsSectionEntryViewModel> GameSectionViewModels { get; } = new([
            new GameLoadoutsSectionEntryDesignViewModel(),
            new GameLoadoutsSectionEntryDesignViewModel(),
        ]
    );
}
