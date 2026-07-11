using System.Reactive;
using Apocrypha.Sdk.Jobs;
using Apocrypha.UI.Sdk;
using ReactiveUI;

namespace Apocrypha.Games.FOMOD.UI;

public interface IFooterStepperViewModel : IViewModelInterface
{
    public Percent Progress { get; set; }

    public bool IsLastStep { get; }

    public bool CanGoNext { get; set; }
    public bool CanGoPrev { get; set; }

    public ReactiveCommand<Unit, Unit> GoToNextCommand { get; }

    public ReactiveCommand<Unit, Unit> GoToPrevCommand { get; }
}
