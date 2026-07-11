using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class LaunchButtonDesignViewModel : AViewModel<ILaunchButtonViewModel>, ILaunchButtonViewModel
{
    [Reactive]
    public LoadoutId LoadoutId { get; set; } = Initializers.LoadoutId;

    [Reactive]
    public ReactiveCommand<Unit, Unit> Command { get; set; }
    
    private BehaviorSubject<bool> _isRunningSubject = new BehaviorSubject<bool>(false);
    public IObservable<bool> IsRunningObservable => _isRunningSubject.AsObservable();

    [Reactive]
    public string Label { get; set; } = "PLAY";

    [Reactive]
    public Percent? Progress { get; set; }

    public LaunchButtonDesignViewModel()
    {
        Command = ReactiveCommand.CreateFromTask(async () =>
        {
            Label = "PREPARING...";
            Progress = Percent.Zero;
            await Task.Delay(100);
            for (var x = 0; x < 10; x++)
            {
                Progress = Percent.CreateClamped(0.1d + Progress!.Value.Value);
                await Task.Delay(200);
            }
            
            _isRunningSubject.OnNext(true);

            Label = "GAME RUNNING...";
            Progress = null;
            await Task.Delay(2000);

            _isRunningSubject.OnNext(false);
            
            Label = "PLAY";
        });
    }
}
