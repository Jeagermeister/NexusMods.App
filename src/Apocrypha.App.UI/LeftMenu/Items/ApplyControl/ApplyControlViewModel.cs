using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games.FileHashes;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Exceptions;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Overlays;
using Apocrypha.App.UI.Overlays.Generic.MessageBox.Ok;
using Apocrypha.App.UI.Pages.Diff.ApplyDiff;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using R3;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Observable = System.Reactive.Linq.Observable;
using ReactiveCommand = ReactiveUI.ReactiveCommand;
using Unit = System.Reactive.Unit;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class ApplyControlViewModel : AViewModel<IApplyControlViewModel>, IApplyControlViewModel
{
    private readonly IConnection _conn;
    private readonly ISynchronizerService _syncService;
    private readonly IJobMonitor _jobMonitor;
    private readonly IWindowNotificationService _notificationService;
    private readonly IGameRegistry _gameRegistry;

    private readonly LoadoutId _loadoutId;
    private readonly IServiceProvider _serviceProvider;
    private readonly GameInstallMetadataId _gameMetadataId;

    // Linux fork: local (login-free) game version recognition.
    private readonly ILocalGameVersionRecognizer? _recognizer;
    private readonly IFileHashesService _fileHashesService;
    private readonly GameInstallation _installation;
    private readonly ILogger<ApplyControlViewModel> _logger;

    [Reactive] private bool CanApply { get; set; } = true;
    [Reactive] public bool IsApplying { get; private set; }
    [Reactive] public bool IsVersionUnknown { get; private set; }
    [Reactive] public bool IsRecognizingVersion { get; private set; }
    [Reactive] public string RecognizingText { get; private set; } = "Recognizing installed version...";

    public ReactiveUI.ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveUI.ReactiveCommand<Unit, Unit> RecognizeVersionCommand { get; }
    public ReactiveUI.ReactiveCommand<NavigationInformation, Unit> ShowApplyDiffCommand { get; }

    [Reactive] public bool IsProcessing { get; private set; }
    [Reactive] public string ApplyButtonText { get; private set; } = Language.ApplyControlViewModel__APPLY;

    [Reactive] public string ProcessingText { get; private set; } = "";
    [Reactive] public bool IsLaunchButtonEnabled { get; private set; } = true;

    public ILaunchButtonViewModel LaunchButtonViewModel { get; }

    public ApplyControlViewModel(LoadoutId loadoutId, IServiceProvider serviceProvider, IJobMonitor jobMonitor, IOverlayController overlayController, GameRunningTracker gameRunningTracker)
    {
        _loadoutId = loadoutId;
        _serviceProvider = serviceProvider;
        _syncService = serviceProvider.GetRequiredService<ISynchronizerService>();
        _conn = serviceProvider.GetRequiredService<IConnection>();
        _jobMonitor = serviceProvider.GetRequiredService<IJobMonitor>();
        _notificationService = serviceProvider.GetRequiredService<IWindowNotificationService>();
        var windowManager = serviceProvider.GetRequiredService<IWindowManager>();
        _gameRegistry = serviceProvider.GetRequiredService<IGameRegistry>();

        var loadout = Sdk.Loadouts.Loadout.Load(_conn.Db, loadoutId);
        _gameMetadataId = loadout.InstallationId;
        _installation = loadout.InstallationInstance;

        // Linux fork: recognizer is optional (only registered when the Steam module is present).
        _fileHashesService = serviceProvider.GetRequiredService<IFileHashesService>();
        _recognizer = serviceProvider.GetService<ILocalGameVersionRecognizer>();
        _logger = serviceProvider.GetRequiredService<ILogger<ApplyControlViewModel>>();

        LaunchButtonViewModel = serviceProvider.GetRequiredService<ILaunchButtonViewModel>();
        LaunchButtonViewModel.LoadoutId = loadoutId;
        
        ApplyCommand = ReactiveCommand.CreateFromTask(async () => await Apply(), 
            canExecute: this.WhenAnyValue(vm => vm.CanApply));

        RecognizeVersionCommand = ReactiveCommand.CreateFromTask(RecognizeVersionAsync,
            canExecute: this.WhenAnyValue(vm => vm.IsVersionUnknown, vm => vm.IsRecognizingVersion, (unknown, recognizing) => unknown && !recognizing));

        ShowApplyDiffCommand = ReactiveCommand.Create<NavigationInformation>(info =>
        {
            var pageData = new PageData
            {
                FactoryId = ApplyDiffPageFactory.StaticId,
                Context = new ApplyDiffPageContext
                {
                    LoadoutId = _loadoutId,
                },
            };

            var workspaceController = windowManager.ActiveWorkspaceController;

            var behavior = workspaceController.GetOpenPageBehavior(pageData, info);
            var workspaceId = workspaceController.ActiveWorkspaceId;
            workspaceController.OpenPage(workspaceId, pageData, behavior);
        });

        this.WhenActivated(disposables =>
            {
                var isProcessingObservable = _jobMonitor.HasActiveJob<ProcessLoadoutChangesJob>(job => job.LoadoutId.Equals(loadoutId))
                    .Prepend(false);
                
                var loadoutStatuses = Observable.Prepend(Observable.FromAsync(() => _syncService.StatusForLoadout(_loadoutId))
                        .Switch(), LoadoutSynchronizerState.Pending);

                var gameStatuses = _syncService.StatusForGame(_gameMetadataId)
                    .Prepend(GameSynchronizerState.Idle);

                var hasUnmanagingJob = _jobMonitor.HasActiveJob<UnmanageGameJob>(job =>
                {
                    if (!_gameRegistry.TryGetMetadata(job.Installation, out var metadata)) return false;
                    return metadata.GameInstallMetadataId == _gameMetadataId;
                }).Prepend(false);

                // Note(sewer):
                // Fire an initial value with StartWith because CombineLatest requires all stuff to have latest values.
                // In any case, we should prevent Apply from being available while a file is in use.
                // A file may be in use because:
                // - The user launched the game externally (e.g. through Steam).
                //     - Approximate this by seeing if any EXE in any of the game folders are running.
                //     - This is done in 'Synchronize' method.
                // - They're running a tool from within the App.
                //     - Check running jobs.
                loadoutStatuses.CombineLatest(isProcessingObservable, gameStatuses, gameRunningTracker.GetWithCurrentStateAsStarting(), hasUnmanagingJob)
                    .OnUI()
                    .Subscribe(status =>
                    {
                        var (ldStatus, isProcessing,  gameStatus, running, isUnmanaging) = status;
                        IsProcessing = isProcessing;
                        CanApply = !isProcessing
                                   && !running
                                   && gameStatus != GameSynchronizerState.Busy
                                   && ldStatus != LoadoutSynchronizerState.Pending
                                   && ldStatus != LoadoutSynchronizerState.Current
                                   && !isUnmanaging;
                        IsLaunchButtonEnabled = !isProcessing 
                                                && !running
                                                && gameStatus != GameSynchronizerState.Busy
                                                && ldStatus == LoadoutSynchronizerState.Current
                                                && !isUnmanaging;
                        
                    })
                    .DisposeWith(disposables);

                _jobMonitor.HasActiveJob<SynchronizeLoadoutJob>(job => job.LoadoutId == loadoutId)
                    .Prepend(_jobMonitor.Jobs.Any(job => job.Definition is SynchronizeLoadoutJob sJob && sJob.LoadoutId == loadoutId))
                    .OnUI()
                    .Subscribe(isApplying => IsApplying = isApplying)
                    .DisposeWith(disposables);
                
                _jobMonitor.ObserveActiveJobs<SynchronizeLoadoutJob>()
                    .Prepend(ChangeSet<IJob, JobId>.Empty)
                    .QueryWhenChanged(jobs =>
                        {
                            if (jobs.Items.FirstOrDefault()?.Definition is SynchronizeLoadoutJob sJob && sJob.LoadoutId == loadoutId)
                                return sJob.StatusMessage.AsSystemObservable();
                            return new BindableReactiveProperty<string>(value: "").AsSystemObservable();
                        }
                    ).Switch()
                    .OnUI()
                    .Subscribe(status => ProcessingText = status)
                    .DisposeWith(disposables);

                // Linux fork: evaluate recognisability only after the hash database is loaded — a lookup
                // against an unloaded database reports every version as unknown and would flash the button
                // for perfectly known games. Re-evaluated on every activation so recognition done elsewhere
                // (CLI, another loadout's footer for the same game) is picked up.
                Observable.FromAsync(async () =>
                        {
                            await _fileHashesService.GetFileHashesDb();
                            return ComputeVersionUnknown();
                        })
                    .OnUI()
                    .Subscribe(unknown => IsVersionUnknown = unknown)
                    .DisposeWith(disposables);

                // Linux fork: recognition runs as a job so it survives navigation; the running state is
                // observed from the job monitor rather than owned by this view model instance. When a run
                // for this game ends (from any view), re-evaluate whether the version is still unknown.
                _jobMonitor.HasActiveJob<RecognizeGameVersionJob>(job => job.Installation.LocatorResult.Path == _installation.LocatorResult.Path)
                    .Prepend(false)
                    .OnUI()
                    .Subscribe(active =>
                    {
                        var wasActive = IsRecognizingVersion;
                        IsRecognizingVersion = active;
                        if (wasActive && !active)
                            IsVersionUnknown = ComputeVersionUnknown();
                    })
                    .DisposeWith(disposables);

                // Show the job's byte-weighted progress in the "Recognizing..." row.
                _jobMonitor.ObserveActiveJobs<RecognizeGameVersionJob>()
                    .Prepend(ChangeSet<IJob, JobId>.Empty)
                    .QueryWhenChanged(jobs =>
                    {
                        var job = jobs.Items.FirstOrDefault(j =>
                            j.Definition is RecognizeGameVersionJob recognize
                            && recognize.Installation.LocatorResult.Path == _installation.LocatorResult.Path);
                        return job?.ObservableProgress ?? Observable.Return(Optional<Percent>.None);
                    })
                    .Switch()
                    .OnUI()
                    .Subscribe(percent => RecognizingText = percent.HasValue
                        ? $"Recognizing installed version... {percent.Value}"
                        : "Recognizing installed version...")
                    .DisposeWith(disposables);
            }
        );
    }

    private async Task Apply()
    {
        var loadout = Sdk.Loadouts.Loadout.Load(_conn.Db, _loadoutId);
        try
        {
            await Task.Run(async () =>
            {
                await _syncService.Synchronize(_loadoutId);
            });
            
            _notificationService.ShowToast(Language.ToastNotification_Applied__0__successfully, ToastNotificationVariant.Success);
        }
        catch (ExecutableInUseException)
        {
            await MessageBoxOkViewModel.ShowGameAlreadyRunningError(_serviceProvider, loadout.Installation.Name);
        }
    }

    /// <summary>
    /// Linux fork: whether the managed game's version is unknown-but-locally-recognisable, which drives
    /// the visibility of the recognize action. Never throws. Only meaningful once the hash database is
    /// loaded (see the activation block), since a lookup against an unloaded database reports every
    /// version as unknown.
    /// </summary>
    private bool ComputeVersionUnknown()
    {
        if (_recognizer is null || !_recognizer.CanRecognize(_installation))
            return false;

        try
        {
            var locatorIds = _installation.LocatorResult.LocatorIds.ToArray();
            var isKnown = _fileHashesService.TryGetVanityVersion((_installation.LocatorResult.Store, locatorIds), out _);
            return !isKnown;
        }
        catch (Exception e)
        {
            // Lookup failed; don't surface the action rather than risk a broken button.
            _logger.LogWarning(e, "Failed to determine whether {Game}'s version is known", _installation.Game.DisplayName);
            return false;
        }
    }

    private async Task RecognizeVersionAsync()
    {
        if (_recognizer is null) return;

        try
        {
            // Runs as a job: it survives navigating away from this loadout, and a second click (or
            // another view for the same game) joins the in-flight run instead of starting a new one.
            // IsRecognizingVersion is driven by the job monitor subscription, not set here.
            var result = await _recognizer.RecognizeInBackground(_installation);
            IsVersionUnknown = ComputeVersionUnknown();

            if (result.AnyRecognized)
            {
                _notificationService.ShowToast(
                    $"Recognized {_installation.Game.DisplayName}: {result.DepotsRecognized} depot(s), {result.TotalVerifiedFiles} verified files.",
                    ToastNotificationVariant.Success);
            }
            else
            {
                _notificationService.ShowToast(
                    $"Couldn't recognize {_installation.Game.DisplayName}. If the install is modified, verify its files in Steam and try again.",
                    ToastNotificationVariant.Neutral);
            }
        }
        catch (OperationCanceledException)
        {
            // The job was cancelled (e.g. app shutdown); nothing to report.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to recognize {Game}'s installed version", _installation.Game.DisplayName);
            _notificationService.ShowToast(
                $"Failed to recognize {_installation.Game.DisplayName}'s version.",
                ToastNotificationVariant.Failure);
        }
    }
}
