using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using OneOf;
using OneOf.Types;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData.Aggregation;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Sdk.Settings;
using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.App.UI.Controls.MiniGameWidget.ComingSoon;
using Apocrypha.App.UI.Controls.MiniGameWidget.Standard;
using Apocrypha.App.UI.Dialog;
using Apocrypha.App.UI.Dialog.Enums;
using Apocrypha.App.UI.Extensions;
using Apocrypha.App.UI.Overlays;
using Apocrypha.App.UI.Pages.LibraryPage;
using Apocrypha.App.UI.Settings;
using Apocrypha.Collections;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Dialog;
using Apocrypha.UI.Sdk.Dialog.Enums;
using GameInstallMetadata = Apocrypha.Sdk.Games.GameInstallMetadata;

namespace Apocrypha.App.UI.Pages.MyGames;

[UsedImplicitly]
public class MyGamesViewModel : APageViewModel<IMyGamesViewModel>, IMyGamesViewModel
{
    private const string TrelloPublicRoadmapUrl = "https://trello.com/b/gPzMuIr3/nexus-mods-app-roadmap";

    private readonly ILibraryService _libraryService;
    private readonly CollectionDownloader _collectionDownloader;
    private readonly IWindowManager _windowManager;
    private readonly IJobMonitor _jobMonitor;
    private readonly IOverlayController _overlayController;
    private readonly IConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISynchronizerService _syncService;
    private readonly ILoadoutManager _loadoutManager;
    private readonly IGameRegistry _gameRegistry;

    private readonly ObservableCollection<IViewModelInterface> _supportedGamesBacking = new();
    private ReadOnlyObservableCollection<IGameWidgetViewModel> _installedGames = new([]);

    public ReactiveCommand<Unit, Unit> OpenRoadmapCommand { get; }
    public ReadOnlyObservableCollection<IGameWidgetViewModel> InstalledGames => _installedGames;
    public ReadOnlyObservableCollection<IViewModelInterface> SupportedGames { get; }

    [Reactive] public string SupportedGamesSearchText { get; set; } = string.Empty;

    public MyGamesViewModel(
        IWindowManager windowManager,
        IServiceProvider serviceProvider,
        IConnection conn,
        ILogger<MyGamesViewModel> logger,
        IOverlayController overlayController,
        IOSInterop osInterop,
        ISynchronizerService syncService,
        IGameRegistry gameRegistry) : base(windowManager)
    {
        var settingsManager = serviceProvider.GetRequiredService<ISettingsManager>();
        var experimentalSettings = settingsManager.Get<ExperimentalSettings>();

        var libraryDataProviders = serviceProvider.GetServices<ILibraryDataProvider>().ToArray();

        _collectionDownloader = serviceProvider.GetRequiredService<CollectionDownloader>();
        _libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        _jobMonitor = serviceProvider.GetRequiredService<IJobMonitor>();
        _overlayController = overlayController;
        _connection = conn;
        _loadoutManager = serviceProvider.GetRequiredService<ILoadoutManager>();
        _gameRegistry = gameRegistry;

        TabTitle = Language.MyGames;
        TabIcon = IconValues.GamepadOutline;
        SupportedGames = new ReadOnlyObservableCollection<IViewModelInterface>(_supportedGamesBacking);

        _serviceProvider = serviceProvider;
        _syncService = syncService;
        _windowManager = windowManager;

        OpenRoadmapCommand = ReactiveCommand.Create(() =>
        {
            var uri = new Uri(TrelloPublicRoadmapUrl);
            osInterop.OpenUri(uri);
        });

        this.WhenActivated(d =>
            {
                gameRegistry.LocateGameInstallations()
                    .Where(game =>
                    {
                        if (experimentalSettings.EnableAllGames) return true;
                        return experimentalSettings.SupportedGames.Contains(game.Game.GameId);
                    })
                    .ToReadOnlyObservableCollection()
                    .ToObservableChangeSet()
                    .Transform(installation =>
                        {
                            var vm = _serviceProvider.GetRequiredService<IGameWidgetViewModel>();
                            vm.Installation = installation;

                            vm.AddGameCommand = ReactiveCommand.CreateFromTask(async () => await AddGameHandler(installation, vm));

                            vm.RemoveAllLoadoutsCommand = ReactiveCommand.CreateFromTask(async () =>
                            {
                                if (GetJobRunningForGameInstallation(installation).IsT2) return;

                                var filesToDelete = libraryDataProviders.SelectMany(dataProvider => dataProvider.GetAllFiles(installation.Game.GameId)).ToArray();
                                var totalSize = filesToDelete.Sum(static Size (file) => file.Size);

                                var collections = installation.Game.NexusModsGameId.HasValue
                                    ? CollectionDownloader.GetCollections(conn.Db, installation.Game.NexusModsGameId.Value)
                                    : [];

                                var overlay = new RemoveGameOverlayViewModel
                                {
                                    GameName = installation.Game.DisplayName,
                                    NumDownloads = filesToDelete.Length,
                                    SumDownloadsSize = totalSize,
                                    NumCollections = collections.Length,
                                };

                                var result = await overlayController.EnqueueAndWait(overlay);
                                if (!result.ShouldRemoveGame) return;

                                vm.State = GameWidgetState.RemovingGame;
                                await Task.Run(async () => await RemoveGame(installation, shouldDeleteDownloads: result.ShouldDeleteDownloads, filesToDelete, collections));
                                vm.State = GameWidgetState.DetectedGame;

                            });

                            vm.ViewGameCommand = ReactiveCommand.Create(() =>
                            {
                                NavigateToLoadoutLibrary(conn, installation);
                            });

                            vm.IsManagedObservable = Loadout.ObserveAll(conn)
                                .Filter(l => l.IsVisible()
                                             && TryGetInstallationInstance(l, out var loadoutInstallation)
                                             && loadoutInstallation!.Game.GameId == installation.Game.GameId
                                             && loadoutInstallation.LocatorResult.Store == installation.LocatorResult.Store)
                                .Count()
                                .Select(c => c > 0);

                            var job = GetJobRunningForGameInstallation(installation);

                            // fixes when the page loads and a job is still running
                            vm.State = job.Value switch
                            {
                                CreateLoadoutJob _ => GameWidgetState.AddingGame,
                                UnmanageGameJob _ => GameWidgetState.RemovingGame,
                                _ => GameWidgetState.DetectedGame,
                            };

                            return vm;
                        }
                    )
                    .OnUI()
                    .Bind(out _installedGames)
                    .SubscribeWithErrorLogging()
                    .DisposeWith(d);

                var supportedGamesAsIGame = serviceProvider
                    .GetServices<IGameData>()
                    .Where(game =>
                    {
                        if (experimentalSettings.EnableAllGames) return true;
                        return experimentalSettings.SupportedGames.Contains(game.GameId);
                    })
                    .Cast<IGame>()
                    .Where(game => _installedGames.All(install => install.Installation?.GetGame().GameId != game.GameId)); // Exclude found games

                var miniGameWidgetViewModels = supportedGamesAsIGame
                    .Select(game =>
                        {
                            var vm = _serviceProvider.GetRequiredService<IMiniGameWidgetViewModel>();
                            vm.Game = game;
                            vm.Name = game.DisplayName;
                            // is this supported game installed?
                            vm.IsFound = _installedGames.Any(install => install.Installation?.GetGame().GameId == game.GameId);
                            vm.GameInstallations = _installedGames
                                .Where(install => install.Installation?.GetGame().GameId == game.GameId)
                                .Select(install => install.Installation)
                                .NotNull()
                                .ToArray();
                            return vm;
                        }
                    )
                    .OrderByDescending(vm => vm.IsFound)
                    .ToList();

                var comingSoonMiniGameWidget = _serviceProvider.GetRequiredService<IComingSoonMiniGameWidgetViewModel>();

                // ~200 supported games since the BepInEx family — filtered by the search box.
                this.WhenAnyValue(vm => vm.SupportedGamesSearchText)
                    .Select(static text => text?.Trim() ?? string.Empty)
                    .DistinctUntilChanged()
                    .OnUI()
                    .Subscribe(searchText =>
                        {
                            _supportedGamesBacking.Clear();
                            foreach (var widget in miniGameWidgetViewModels)
                            {
                                if (searchText.Length == 0 || widget.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                                    _supportedGamesBacking.Add(widget);
                            }

                            // Always last: it doubles as the no-results outcome ("your game
                            // may be coming") and keeps the request-a-game path visible.
                            _supportedGamesBacking.Add(comingSoonMiniGameWidget);
                        }
                    )
                    .DisposeWith(d);
            }
        );
    }

    private OneOf<None, CreateLoadoutJob, UnmanageGameJob> GetJobRunningForGameInstallation(GameInstallation installation)
    {
        foreach (var job in _jobMonitor.Jobs)
        {
            if (job.Status != JobStatus.Running) continue;

            if (job.Definition is CreateLoadoutJob createLoadoutJob && createLoadoutJob.Installation.Equals(installation)) return createLoadoutJob;
            if (job.Definition is UnmanageGameJob unmanageGameJob && unmanageGameJob.Installation.Equals(installation)) return unmanageGameJob;
        }

        return OneOf<None, CreateLoadoutJob, UnmanageGameJob>.FromT0(new None());
    }

    private async Task RemoveGame(GameInstallation installation, bool shouldDeleteDownloads, LibraryFile.ReadOnly[] filesToDelete, CollectionMetadata.ReadOnly[] collections)
    {
        await _syncService.UnManage(installation);

        if (!shouldDeleteDownloads) return;
        await _libraryService.RemoveLibraryItems(filesToDelete.Select(file => file.AsLibraryItem()));

        foreach (var collection in collections)
        {
            await _collectionDownloader.DeleteCollection(collection);
        }
    }
    
    private async Task AddGameHandler(GameInstallation installation, IGameWidgetViewModel vm)
    {
        if (GetJobRunningForGameInstallation(installation).IsT1) return;

        vm.State = GameWidgetState.AddingGame;
        var loadout = await Task.Run(async () => await ManageGame(installation));
        
        // Check if there are external changes
        var changeEntries = await GetExternalChangesItems(loadout);
        vm.State = GameWidgetState.ManagedGame;
        
        // Offer to clean them up
        if (changeEntries.Length > 0)
        {
            var (revert, doNothing, clean) = (ButtonDefinitionId.Cancel, ButtonDefinitionId.From("doNothing"), ButtonDefinitionId.Accept);
            var result = await ShowCleanGameFolderDialog(revert,
                doNothing,
                clean,
                changeEntries,
                installation
            );
            
            if (result == revert)
            {
                // Revert the loadout creation
                vm.State = GameWidgetState.RemovingGame;
                await Task.Run(async () => await _syncService.UnManage(installation, cleanGameFolder: false));
                vm.State = GameWidgetState.DetectedGame;
                
                return;
            }
            if (result == clean)
            {
                vm.State = GameWidgetState.AddingGame;
                await CleanGameFolder(installation, loadout);
                vm.State = GameWidgetState.ManagedGame;
                
            }
            
            // do nothing, so keep the files
        }
        
        NavigateToLoadoutLibrary(_connection, installation);
    }
    
    private ValueTask<LoadoutItemWithTargetPath.ReadOnly[]> GetExternalChangesItems(Loadout.ReadOnly loadout)
    {
        var db = _connection.Db;
        if (!LoadoutOverridesGroup.FindByOverridesFor(db, loadout.Id).TryGetFirst(out var overrideGroup))
            return ValueTask.FromResult<LoadoutItemWithTargetPath.ReadOnly[]>([]);

        return ValueTask.FromResult(overrideGroup.AsLoadoutItemGroup().Children.OfTypeLoadoutItemWithTargetPath().ToArray());
    }
    
    private async Task<ButtonDefinitionId> ShowCleanGameFolderDialog(
        ButtonDefinitionId revert,
        ButtonDefinitionId doNothing,
        ButtonDefinitionId clean,
        LoadoutItemWithTargetPath.ReadOnly[] changeEntries,
        GameInstallation installation)
    {
        var markdownVm = _serviceProvider.GetRequiredService<IMarkdownRendererViewModel>();
        markdownVm.Contents = $"""
            We found {changeEntries.Length} files in the game folder that aren’t part of a clean install. To avoid conflicts with mods, **we recommend starting with a clean folder**.
            #### Important
            If you keep existing files (not recommended):
            - The existing files will be placed in **External Changes**.
            - Files in External Changes **override any mods you install later**.
            - If you stop managing this game or uninstall the app, those **files will be permanently removed**.
            """;
        
        var dialog = DialogFactory.CreateStandardDialog(
            title: $"Your {installation.Game.DisplayName} folder isn't a clean install",
            new StandardDialogParameters()
            {
                Markdown = markdownVm,
            },
            buttonDefinitions:
            [
                new DialogButtonDefinition("Cancel", revert),
                new DialogButtonDefinition("Keep existing files", doNothing, ButtonAction.Reject),
                new DialogButtonDefinition("Clean folder", clean, ButtonAction.Accept, ButtonStyling.Primary),
            ]
        );
        
        return (await _windowManager.ShowDialog(dialog, DialogWindowType.Modal)).ButtonId;
    }
    
    private async Task CleanGameFolder(GameInstallation installation, Loadout.ReadOnly loadout)
    {
        var db = _connection.Db;
        var tx = _connection.BeginTransaction();
        var changeEntries = await GetExternalChangesItems(loadout.Rebase()); 
        
        // Remove items from External Changes mod
        foreach (var entry in changeEntries)
        {
            tx.Delete(entry.Id, recursive: false);
        }
        await tx.Commit();

        loadout = loadout.Rebase();
        var game = installation.GetGame();
        var syncer = game.Synchronizer;
        
        // Apply clean state to game folder
        await syncer.Synchronize(Loadout.Load(db, loadout));
    }

    private async Task<Loadout.ReadOnly> ManageGame(GameInstallation installation)
    {
        return await _loadoutManager.CreateLoadout(installation);
    }

    private Optional<LoadoutId> GetLoadout(IConnection conn, GameInstallation installation)
    {
        if (!_gameRegistry.TryGetMetadata(installation, out var metadata)) return Optional<LoadoutId>.None;
        if (metadata.Contains(GameInstallMetadata.LastSyncedLoadout))
        {
            return metadata.LastSyncedLoadout.LoadoutId;
        }

        // no applied loadout, return the first one
        var loadout = Loadout.All(conn.Db).FirstOrOptional(loadout =>
            loadout.IsVisible()
            && TryGetInstallationInstance(loadout, out var loadoutInstallation)
            && loadoutInstallation!.Equals(installation));
        return loadout.HasValue ? loadout.Value.LoadoutId : Optional<LoadoutId>.None;
    }
    
    private void NavigateToLoadoutLibrary(IConnection conn, GameInstallation installation)
    {
        var fistLoadout = GetLoadout(conn, installation);
        if (!fistLoadout.HasValue) return;
        var loadoutId = fistLoadout.Value;
        Dispatcher.UIThread.Invoke(() =>
            {
                var workspaceController = _windowManager.ActiveWorkspaceController;
                
                workspaceController.ChangeOrCreateWorkspaceByContext(
                    context => context.LoadoutId == loadoutId,
                    () => new PageData
                    {
                        FactoryId = LibraryPageFactory.StaticId,
                        Context = new LibraryPageContext()
                        {
                            LoadoutId = loadoutId,
                        },
                    },
                    () => new LoadoutContext
                    {
                        LoadoutId = loadoutId,
                    }
                );
            }
        );
    }

    /// <summary>
    /// Safe variant of <see cref="Loadout.ReadOnly.InstallationInstance"/>: loadouts whose game
    /// installation can no longer be located (uninstalled, moved, or a stale manage of a broken
    /// install) are reported as unmatched instead of throwing and killing the reactive pipeline.
    /// </summary>
    private static bool TryGetInstallationInstance(Loadout.ReadOnly loadout, out GameInstallation? installation)
    {
        var gameRegistry = loadout.Db.Connection.ServiceProvider.GetRequiredService<IGameRegistry>();
        return gameRegistry.TryGetGameInstallation(loadout, out installation);
    }
}
