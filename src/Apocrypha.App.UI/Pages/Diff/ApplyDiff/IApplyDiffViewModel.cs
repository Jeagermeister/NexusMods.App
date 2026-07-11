using System.Reactive;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Ids;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.Diff.ApplyDiff;

public interface IApplyDiffViewModel : IPageViewModelInterface
{
    public void Initialize(LoadoutId loadoutId);
    
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    
    public IViewModelInterface BodyViewModel { get; set; }
}
