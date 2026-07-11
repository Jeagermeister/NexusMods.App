using System.Reactive;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.App.UI.LeftMenu.Items;

public interface IApplyControlViewModel : IViewModelInterface
{
    ReactiveCommand<Unit,Unit> ApplyCommand { get; }
    
    ReactiveCommand<NavigationInformation, Unit> ShowApplyDiffCommand { get; }
    
    ILaunchButtonViewModel LaunchButtonViewModel { get; }
    
    bool IsLaunchButtonEnabled { get; }
    
    bool IsProcessing { get; }
    
    bool IsApplying { get; }
    
    string ApplyButtonText { get; }
    
    string ProcessingText { get; }

    /// <summary>
    /// Linux fork: recognises the installed game's version locally (login-free) and records it in the
    /// local hash overlay so the synchronizer stops treating a vanilla install as fully modified.
    /// </summary>
    ReactiveCommand<Unit, Unit> RecognizeVersionCommand { get; }

    /// <summary>
    /// True when the managed game is a Steam install whose version is not recognised by the hash
    /// database and can be recognised locally. Controls visibility of the recognise action.
    /// </summary>
    bool IsVersionUnknown { get; }

    /// <summary>True while a local recognition run is in progress (for this game, from any view).</summary>
    bool IsRecognizingVersion { get; }

    /// <summary>Status text for the in-progress recognition row, including the job's progress.</summary>
    string RecognizingText { get; }
}
