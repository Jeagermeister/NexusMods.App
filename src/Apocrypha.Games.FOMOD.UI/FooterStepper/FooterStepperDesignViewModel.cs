using System.Reactive.Disposables;
using System.Reactive.Linq;
using Apocrypha.App.UI;
using Apocrypha.Sdk.Jobs;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.Games.FOMOD.UI;

public class FooterStepperDesignViewModel : FooterStepperViewModel
{
    [Reactive] private int CurrentValue { get; set; } = 5;

    public FooterStepperDesignViewModel()
    {
        var canGoToNext = this
            .WhenAnyValue(vm => vm.CurrentValue)
            .Select(currentValue => currentValue < 10);

        GoToNextCommand = ReactiveCommand.Create(() => { CurrentValue += 1; }, canGoToNext);

        var canGoToPrev = this
            .WhenAnyValue(vm => vm.CurrentValue)
            .Select(currentValue => currentValue > 0);

        GoToPrevCommand = ReactiveCommand.Create(() => { CurrentValue -= 1; }, canGoToPrev);

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(vm => vm.CurrentValue)
                .Select(currentValue => Percent.Create(current: currentValue, maximum: 10))
                .BindToVM(this, vm => vm.Progress)
                .DisposeWith(disposables);
        });
    }
}
