using System.Reactive;
using Avalonia.Media;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.LoadoutCard;

public interface ILoadoutCardViewModel : IViewModelInterface
{
    ILoadoutBadgeViewModel LoadoutBadgeViewModel { get; }
    
    string LoadoutName { get; }
    
    IImage? LoadoutImage { get; }
    
    bool IsLoadoutApplied { get; }
    
    string HumanizedLoadoutLastApplyTime { get; }
    
    string HumanizedLoadoutCreationTime { get; }
    
    string LoadoutModCount { get; }
    
    bool IsDeleting { get; }
    
    bool IsSkeleton { get; }
    
    bool IsLastLoadout { get; }
    
    ReactiveCommand<Unit, Unit> VisitLoadoutCommand { get; }
    
    ReactiveCommand<Unit, Unit> CloneLoadoutCommand { get; }
    
    ReactiveCommand<Unit, Unit> DeleteLoadoutCommand { get; }
}
