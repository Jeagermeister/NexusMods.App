using System.Reactive;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Resources;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class ApplyControlDesignViewModel : AViewModel<IApplyControlViewModel>, IApplyControlViewModel
{
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> IngestCommand { get; }
    public ReactiveCommand<Unit, Unit> RecognizeVersionCommand { get; }

    public ReactiveCommand<NavigationInformation, Unit> ShowApplyDiffCommand { get; }
    [Reactive] public bool CanApply { get; private set; } = true;
    [Reactive] public bool IsApplying { get; private set; } = false;
    public bool IsIngesting { get; private set; }
    public bool IsVersionUnknown { get; } = true;
    public bool IsRecognizingVersion { get; } = false;
    public string RecognizingText { get; } = "Recognizing installed version... 42%";

    public ILaunchButtonViewModel LaunchButtonViewModel { get; } = new LaunchButtonDesignViewModel();
    public bool IsLaunchButtonEnabled { get; } = true;
    public bool IsProcessing { get; } = false;
    public string ApplyButtonText { get; } = Language.ApplyControlViewModel__APPLY;

    public string ProcessingText { get; } = "PROCESSING TEXT";

    public ApplyControlDesignViewModel()
    {
        ShowApplyDiffCommand = ReactiveCommand.Create<NavigationInformation>(_ => { });
        RecognizeVersionCommand = ReactiveCommand.Create(() => { });

        ApplyCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                IsApplying = true;
                CanApply = false;

                await Task.Delay(3000);

                IsApplying = false;
                CanApply = true;
            }
        );

        IngestCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                IsIngesting = true;
                CanApply = false;

                await Task.Delay(3000);

                IsIngesting = false;
                CanApply = true;
            }
        );
    }
}
