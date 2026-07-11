using DynamicData.Kernel;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Controls.LoadoutBadge;

public interface ILoadoutBadgeViewModel : IViewModelInterface
{
    Optional<Loadout.ReadOnly> LoadoutValue { get; set;  }
    
    string LoadoutShortName { get; }
    
    bool IsLoadoutSelected { get; set; }
    
    bool IsLoadoutApplied { get; }
    
    bool IsLoadoutInProgress { get; }
    
    bool IsVisible { get; }
}
