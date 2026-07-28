using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Exceptions;
using Apocrypha.App.UI.Overlays;
using Apocrypha.App.UI.Overlays.Generic.MessageBox.Ok;
using Apocrypha.App.UI.Resources;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class LaunchButtonViewModel : AViewModel<ILaunchButtonViewModel>, ILaunchButtonViewModel
{
    [Reactive] public LoadoutId LoadoutId { get; set; } = Initializers.LoadoutId;

    [Reactive] public ReactiveCommand<Unit, Unit> Command { get; set; }

    public IObservable<bool> IsRunningObservable => _gameRunningTracker.GetWithCurrentStateAsStarting();

    [Reactive] public string Label { get; set; } = Language.LaunchButtonViewModel_LaunchGame_LAUNCH;

    [Reactive] public Percent? Progress { get; set; }

    private readonly ILogger<ILaunchButtonViewModel> _logger;
    private readonly IToolManager _toolManager;
    private readonly IConnection _conn;
    private readonly IJobMonitor _monitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly GameRunningTracker _gameRunningTracker;

    public LaunchButtonViewModel(ILogger<ILaunchButtonViewModel> logger, IToolManager toolManager, IConnection conn, IJobMonitor monitor, IServiceProvider serviceProvider, GameRunningTracker gameRunningTracker)
    {
        _logger = logger;
        _toolManager = toolManager;
        _conn = conn;
        _monitor = monitor;
        _serviceProvider = serviceProvider;
        _gameRunningTracker = gameRunningTracker;
        
        this.WhenActivated(cd =>
        {
            _gameRunningTracker.GetWithCurrentStateAsStarting().Subscribe(isRunning =>
            {
                if (isRunning)
                    SetLabelToRunning();
                else
                    SetLabelToLaunch();
            }).DisposeWith(cd);
        });

        Command = ReactiveCommand.CreateFromObservable(() => Observable.StartAsync(LaunchGame, RxApp.TaskpoolScheduler));
    }

    private async Task LaunchGame(CancellationToken token)
    {
        var marker = Sdk.Loadouts.Loadout.Load(_conn.Db, LoadoutId);
        SetLabelToRunning();
        try
        {
            var tool = _toolManager.GetTools(marker).OfType<IRunGameTool>().First();
            await Task.Run(async () =>
            {
                await _toolManager.RunTool(tool, marker, _monitor, token: token);
            }, token);
        }
        catch (ExecutableInUseException)
        {
            await MessageBoxOkViewModel.ShowGameAlreadyRunningError(_serviceProvider, marker.Installation.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error launching game");
            // A launch that fails must never be silent -- without this the button just flips
            // back to LAUNCH (e.g. exec of a Windows PE on Linux for Heroic/GOG titles).
            await MessageBoxOkViewModel.Show(_serviceProvider,
                title: $"Failed to launch {marker.Installation.Name}",
                description: $"The game could not be started: {ex.Message}\n\nSee the log file for details.");
        }
        SetLabelToLaunch();
    }
    private void SetLabelToRunning() => Label = Language.LaunchButtonViewModel_LaunchGame_RUNNING;
    private void SetLabelToLaunch() => Label = Language.LaunchButtonViewModel_LaunchGame_LAUNCH;
}
