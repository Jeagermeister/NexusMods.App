using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Sdk.EventBus;
using Apocrypha.Abstractions.Serialization.ExpressionGenerator;
using Apocrypha.Abstractions.Serialization.Json;
using Apocrypha.App.UI.Controls.DevelopmentBuildBanner;
using Apocrypha.App.UI.Controls.Diagnostics;
using Apocrypha.App.UI.Controls.GameWidget;
using Apocrypha.App.UI.Controls.LoadoutBadge;
using Apocrypha.App.UI.Controls.LoadoutCard;
using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.App.UI.Controls.MiniGameWidget;
using Apocrypha.App.UI.Controls.MiniGameWidget.ComingSoon;
using Apocrypha.App.UI.Controls.MiniGameWidget.Standard;
using Apocrypha.App.UI.Controls.Settings.Section;
using Apocrypha.App.UI.Controls.Settings.SettingEntries;
using Apocrypha.App.UI.Controls.Settings.SettingEntries.PathsList;
using Apocrypha.App.UI.Controls.Spine;
using Apocrypha.App.UI.Controls.Spine.Buttons.Download;
using Apocrypha.App.UI.Controls.Spine.Buttons.Icon;
using Apocrypha.App.UI.Controls.Spine.Buttons.Image;
using Apocrypha.App.UI.Controls.TopBar;
using Apocrypha.App.UI.Controls.Trees;
using Apocrypha.App.UI.Controls.Trees.Files;
using Apocrypha.App.UI.DiagnosticSystem;
using Apocrypha.App.UI.Dialog;
using Apocrypha.App.UI.LeftMenu;
using Apocrypha.App.UI.LeftMenu.Downloads;
using Apocrypha.App.UI.LeftMenu.Home;
using Apocrypha.App.UI.LeftMenu.Items;
using Apocrypha.App.UI.LeftMenu.Loadout;
using Apocrypha.App.UI.Notifications;
using Apocrypha.App.UI.Overlays;
using Apocrypha.App.UI.Overlays.Generic.MessageBox.Ok;
using Apocrypha.App.UI.Overlays.Generic.MessageBox.OkCancel;
using Apocrypha.App.UI.Overlays.LibraryDeleteConfirmation;
using Apocrypha.App.UI.Overlays.Login;
using Apocrypha.App.UI.Overlays.Updater;
using Apocrypha.App.UI.Pages;
using Apocrypha.App.UI.Pages.Changelog;
using Apocrypha.App.UI.Pages.CollectionDownload;
using Apocrypha.App.UI.Pages.CollectionDownload.Dialogs.PremiumDownloads;
using Apocrypha.App.UI.Pages.DebugControls;
using Apocrypha.App.UI.Pages.Diagnostics;
using Apocrypha.App.UI.Pages.Diff.ApplyDiff;
using Apocrypha.App.UI.Pages.Downloads;
using Apocrypha.App.UI.Pages.LibraryPage;
using Apocrypha.App.UI.Pages.LibraryPage.Collections;
using Apocrypha.App.UI.Pages.LibraryPage.ModSources;
using Apocrypha.Abstractions.ModSources;
using Apocrypha.App.UI.Pages.LoadoutGroupFilesPage;
using Apocrypha.App.UI.Pages.LoadoutPage;
using Apocrypha.App.UI.Pages.LoadoutPage.Dialogs;
using Apocrypha.App.UI.Pages.HomeDashboard;
using Apocrypha.App.UI.Pages.LoadoutPage.Dialogs.CollectionPublished;
using Apocrypha.App.UI.Pages.LoadoutPage.Dialogs.ShareCollection;
using Apocrypha.App.UI.Pages.MyGames;
using Apocrypha.App.UI.Pages.MyLoadouts;
using Apocrypha.App.UI.Pages.ObservableInfo;
using Apocrypha.App.UI.Pages.Settings;
using Apocrypha.App.UI.Pages.Sorting;
using Apocrypha.App.UI.Pages.TextEdit;
using Apocrypha.App.UI.Settings;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceAttachments;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.Paths;
using Apocrypha.Sdk.Settings;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Settings;
using ReactiveUI;
using ImageButton = Apocrypha.App.UI.Controls.Spine.Buttons.Image.ImageButton;
using NexusLoginOverlayView = Apocrypha.App.UI.Overlays.Login.NexusLoginOverlayView;
using SettingToggleControl = Apocrypha.App.UI.Controls.Settings.SettingEntries.SettingToggleControl;

namespace Apocrypha.App.UI;

public static class Services
{
    // ReSharper disable once InconsistentNaming
    public static IServiceCollection AddUI(this IServiceCollection c)
    {
        return c
            // JSON converters
            .AddSingleton<JsonConverter, RectJsonConverter>()
            .AddSingleton<JsonConverter, ColorJsonConverter>()
            .AddSingleton<JsonConverter, AbstractClassConverterFactory<IPageFactoryContext>>()
            .AddSingleton<JsonConverter, AbstractClassConverterFactory<IWorkspaceContext>>()

            // Type Finder
            .AddSingleton<ITypeFinder, TypeFinder>()
            .AddSingleton<GameRunningTracker>()
            .AddTransient<MainWindow>()

            // Services
            .AddSingleton<IOverlayController, OverlayController>()
            .AddSingleton<IWindowNotificationService, WindowNotificationService>()

            // View Models
            .AddTransient<MainWindowViewModel>()
            .AddSingleton<IViewLocator, InjectedViewLocator>()
            
            .AddViewModel<CollectionCardDesignViewModel, ICollectionCardViewModel>()

            .AddViewModel<DevelopmentBuildBannerViewModel, IDevelopmentBuildBannerViewModel>()
            .AddViewModel<GameWidgetViewModel, IGameWidgetViewModel>()
            .AddViewModel<MiniGameWidgetViewModel, IMiniGameWidgetViewModel>()
            .AddViewModel<ComingSoonMiniGameWidgetViewModel, IComingSoonMiniGameWidgetViewModel>()
            .AddViewModel<HomeLeftMenuViewModel, IHomeLeftMenuViewModel>()
            .AddViewModel<IconButtonViewModel, IIconButtonViewModel>()
            .AddViewModel<LeftMenuItemViewModel, ILeftMenuItemViewModel>()
            .AddViewModel<CollectionLeftMenuItemViewModel, ILeftMenuItemViewModel>()
            .AddViewModel<ImageButtonViewModel, IImageButtonViewModel>()
            .AddViewModel<LaunchButtonViewModel, ILaunchButtonViewModel>()
            .AddViewModel<ApplyControlViewModel, IApplyControlViewModel>()
            .AddViewModel<MyGamesViewModel, IMyGamesViewModel>()
            .AddViewModel<HomeDashboardViewModel, IHomeDashboardViewModel>()
            .AddViewModel<NexusLoginOverlayViewModel, INexusLoginOverlayViewModel>()
            .AddViewModel<SpineViewModel, ISpineViewModel>()
            .AddViewModel<TopBarViewModel, ITopBarViewModel>()
            .AddViewModel<SpineDownloadButtonViewModel, ISpineDownloadButtonViewModel>()
            .AddViewModel<MessageBoxOkViewModel, IMessageBoxOkViewModel>()
            .AddViewModel<MessageBoxOkCancelViewModel, IMessageBoxOkCancelViewModel>()
            .AddViewModel<UpdaterViewModel, IUpdaterViewModel>()
            .AddViewModel<LoadoutLeftMenuViewModel, ILoadoutLeftMenuViewModel>()
            .AddViewModel<DownloadsLeftMenuViewModel, IDownloadsLeftMenuViewModel>()
            .AddViewModel<FileTreeNodeViewModel, IFileTreeNodeViewModel>()
            .AddViewModel<ApplyDiffViewModel, IApplyDiffViewModel>()

            // Views
            .AddView<CollectionCardView, ICollectionCardViewModel>()
            .AddView<DevelopmentBuildBannerView, IDevelopmentBuildBannerViewModel>()
            .AddView<GameWidget, IGameWidgetViewModel>()
            .AddView<MiniGameWidget, IMiniGameWidgetViewModel>()
            .AddView<ComingSoonMiniGameWidget, IComingSoonMiniGameWidgetViewModel>()
            .AddView<HomeLeftMenuView, IHomeLeftMenuViewModel>()
            .AddView<IconButton, IIconButtonViewModel>()
            .AddView<LeftMenuItemView, ILeftMenuItemViewModel>()
            .AddView<ImageButton, IImageButtonViewModel>()
            .AddView<LaunchButtonView, ILaunchButtonViewModel>()
            .AddView<MyGamesView, IMyGamesViewModel>()
            .AddView<HomeDashboardView, IHomeDashboardViewModel>()
            .AddView<NexusLoginOverlayView, INexusLoginOverlayViewModel>()
            .AddView<Spine, ISpineViewModel>()
            .AddView<TopBarView, ITopBarViewModel>()
            .AddView<SpineDownloadButtonView, ISpineDownloadButtonViewModel>()
            .AddView<MessageBoxOkView, IMessageBoxOkViewModel>()
            .AddView<MessageBoxOkCancelView, IMessageBoxOkCancelViewModel>()
            .AddView<UpdaterView, IUpdaterViewModel>()
            .AddView<LoadoutLeftMenuView, ILoadoutLeftMenuViewModel>()
            .AddView<DownloadsLeftMenuView, IDownloadsLeftMenuViewModel>()
            .AddView<ApplyControlView, IApplyControlViewModel>()
            .AddView<FileTreeNodeView, IFileTreeNodeViewModel>()
            .AddView<ApplyDiffView, IApplyDiffViewModel>()
            .AddView<FileTreeView, IFileTreeViewModel>()
            
            
            .AddView<MyLoadoutsView, IMyLoadoutsViewModel>()
            .AddViewModel<MyLoadoutsViewModel, IMyLoadoutsViewModel>()
            .AddView<LoadoutCardView, ILoadoutCardViewModel>()
            .AddView<CreateNewLoadoutCardView, ICreateNewLoadoutCardViewModel>()
            .AddViewModel<LoadoutBadgeViewModel, ILoadoutBadgeViewModel>()
            
            .AddView<SettingsView, ISettingsPageViewModel>()
            .AddViewModel<SettingsPageViewModel, ISettingsPageViewModel>()

            .AddView<SettingSectionView, ISettingSectionViewModel>()
            .AddViewModel<SettingSectionViewModel, ISettingSectionViewModel>()

            .AddView<SettingEntryView, ISettingEntryViewModel>()
            .AddViewModel<SettingEntryViewModel, ISettingEntryViewModel>()
            .AddView<SettingToggleControl, ISettingToggleViewModel>()
            .AddViewModel<SettingToggleViewModel, ISettingToggleViewModel>()
            .AddView<SettingComboBoxView, ISettingComboBoxViewModel>()
            .AddViewModel<SettingComboBoxViewModel, ISettingComboBoxViewModel>()
            .AddView<SettingPathsControl, ISettingPathsViewModel>()
            .AddViewModel<SettingPathsViewModel, ISettingPathsViewModel>()

            .AddView<DiagnosticEntryView, IDiagnosticEntryViewModel>()
            .AddViewModel<DiagnosticEntryViewModel, IDiagnosticEntryViewModel>()
            .AddView<DiagnosticListView, IDiagnosticListViewModel>()
            .AddViewModel<DiagnosticListViewModel, IDiagnosticListViewModel>()
            .AddView<DiagnosticDetailsView, IDiagnosticDetailsViewModel>()
            .AddViewModel<DiagnosticDetailsViewModel, IDiagnosticDetailsViewModel>()

            .AddView<MarkdownRendererView, IMarkdownRendererViewModel>()
            .AddViewModel<MarkdownRendererViewModel, IMarkdownRendererViewModel>()
            .AddView<ChangelogPageView, IChangelogPageViewModel>()
            .AddViewModel<ChangelogPageViewModel, IChangelogPageViewModel>()

            .AddView<TextEditorPageView, ITextEditorPageViewModel>()
            .AddViewModel<TextEditorPageViewModel, ITextEditorPageViewModel>()

            .AddView<LibraryItemDeleteConfirmationView, ILibraryItemDeleteConfirmationViewModel>()
            .AddViewModel<LibraryItemDeleteConfirmationViewModel, ILibraryItemDeleteConfirmationViewModel>()

            .AddView<LibraryView, ILibraryViewModel>()
            .AddView<DownloadsPageView, IDownloadsPageViewModel>()
            .AddView<LoadoutView, ILoadoutViewModel>()

            .AddView<CollectionDownloadView, ICollectionDownloadViewModel>()
            .AddViewModel<CollectionDownloadViewModel, ICollectionDownloadViewModel>()
            
            .AddView<LoadOrderView, ILoadOrderViewModel>()

            .AddView<UpgradeToPremiumView, IUpgradeToPremiumViewModel>()
            .AddViewModel<UpgradeToPremiumViewModel, IUpgradeToPremiumViewModel>()

            .AddView<CollectionLoadoutView, ICollectionLoadoutViewModel>()
            .AddViewModel<CollectionLoadoutViewModel, ICollectionLoadoutViewModel>()

            .AddView<ObservableInfoPageView, IObservableInfoPageViewModel>()
            .AddViewModel<ObservableInfoPageViewModel, IObservableInfoPageViewModel>()
            
            .AddView<DebugControlsPageView, IDebugControlsPageViewModel>()
            .AddViewModel<DebugControlsPageViewModel, IDebugControlsPageViewModel>()

            .AddView<ManualDownloadRequiredOverlayView, IManualDownloadRequiredOverlayViewModel>()
            .AddViewModel<ManualDownloadRequiredOverlayViewModel, IManualDownloadRequiredOverlayViewModel>()

            .AddView<RemoveGameOverlayView, IRemoveGameOverlayViewModel>()
            .AddViewModel<RemoveGameOverlayViewModel, IRemoveGameOverlayViewModel>()

            .AddView<WelcomeOverlayView, IWelcomeOverlayViewModel>()
            .AddViewModel<WelcomeOverlayViewModel, IWelcomeOverlayViewModel>()
            
            // Dialogs
            .AddView<DialogStandardContentView, IDialogStandardContentViewModel>()
            .AddViewModel<DialogStandardContentViewModel, IDialogStandardContentViewModel>()
            .AddView<DialogShareCollectionView, IDialogShareCollectionViewModel>()
            .AddViewModel<DialogShareCollectionViewModel, IDialogShareCollectionViewModel>()
            .AddView<DialogCollectionPublishedView, IDialogCollectionPublishedViewModel>()
            .AddViewModel<DialogCollectionPublishedViewModel, IDialogCollectionPublishedViewModel>()
            
            .AddView<DialogPremiumCollectionDownloadsView, IDialogPremiumCollectionDownloadsViewModel>()
            .AddViewModel<DialogPremiumCollectionDownloadsViewModel, IDialogPremiumCollectionDownloadsViewModel>()

            .AddView<ProtocolRegistrationTestPageView, IProtocolRegistrationTestPageViewModel>()
            .AddViewModel<ProtocolRegistrationTestPageViewModel, IProtocolRegistrationTestPageViewModel>()

            .AddView<LoadoutGroupFilesView, ILoadoutGroupFilesViewModel>()
            .AddViewModel<LoadoutGroupFilesViewModel, ILoadoutGroupFilesViewModel>()

            // workspace system
            .AddSingleton<IWindowManager, WindowManager>()
            .AddWindowDataAttributesModel()
            .AddViewModel<WorkspaceViewModel, IWorkspaceViewModel>()
            .AddViewModel<PanelViewModel, IPanelViewModel>()
            .AddViewModel<AddPanelButtonViewModel, IAddPanelButtonViewModel>()
            .AddViewModel<AddPanelDropDownViewModel, IAddPanelDropDownViewModel>()
            .AddViewModel<PanelTabHeaderViewModel, IPanelTabHeaderViewModel>()
            .AddViewModel<NewTabPageViewModel, INewTabPageViewModel>()
            .AddViewModel<NewTabPageSectionViewModel, INewTabPageSectionViewModel>()
            .AddView<WorkspaceView, IWorkspaceViewModel>()
            .AddView<PanelView, IPanelViewModel>()
            .AddView<AddPanelButtonView, IAddPanelButtonViewModel>()
            .AddView<AddPanelDropDownView, IAddPanelDropDownViewModel>()
            .AddView<PanelTabHeaderView, IPanelTabHeaderViewModel>()
            .AddView<NewTabPageView, INewTabPageViewModel>()

            // page factories
            .AddSingleton<PageFactoryController>()
            .AddSingleton<IPageFactory, NewTabPageFactory>()
            .AddSingleton<IPageFactory, MyGamesPageFactory>()
            .AddSingleton<IPageFactory, HomeDashboardPageFactory>()
            .AddSingleton<IPageFactory, DiagnosticListPageFactory>()
            .AddSingleton<IPageFactory, DiagnosticDetailsPageFactory>()
            .AddSingleton<IPageFactory, ApplyDiffPageFactory>()
            .AddSingleton<IPageFactory, SettingsPageFactory>()
            .AddSingleton<IPageFactory, ChangelogPageFactory>()
            .AddSingleton<IPageFactory, TextEditorPageFactory>()
            .AddSingleton<IPageFactory, MyLoadoutsPageFactory>()
            .AddSingleton<IPageFactory, LibraryPageFactory>()
            .AddSingleton<IPageFactory, DownloadsPageFactory>()
            .AddSingleton<IPageFactory, LoadoutPageFactory>()
            .AddSingleton<IPageFactory, CollectionDownloadPageFactory>()
            .AddSingleton<IPageFactory, CollectionLoadoutPageFactory>()
            .AddSingleton<IPageFactory, ObservableInfoPageFactory>()
            .AddSingleton<IPageFactory, DebugControlsPageFactory>()
            .AddSingleton<IPageFactory, ProtocolRegistrationTestPageFactory>()
            .AddSingleton<IPageFactory, LoadoutGroupFilesPageFactory>()

            // LeftMenu factories
            .AddSingleton<ILeftMenuFactory, HomeLeftMenuFactory>()
            .AddSingleton<ILeftMenuFactory, LoadoutLeftMenuFactory>()
            .AddSingleton<ILeftMenuFactory, DownloadsLeftMenuFactory>()

            // Workspace Attachments
            .AddSingleton<IWorkspaceAttachmentsFactoryManager, WorkspaceAttachmentsFactoryManager>()
            .AddSingleton<IWorkspaceAttachmentsFactory, DownloadsAttachmentsFactory>()
            .AddSingleton<IWorkspaceAttachmentsFactory, HomeAttachmentsFactory>()
            .AddSingleton<IWorkspaceAttachmentsFactory, LoadoutAttachmentsFactory>()

            // Diagnostics
            .AddDiagnosticWriter()

            // Overlay Helpers
            .AddHostedService<NexusLoginOverlayService>()

            // Settings
            .AddUISettings()
            .AddSingleton<IInteractionControlFactory<SingleValueMultipleChoiceContainerOptions>, SettingComboBoxFactory>()
            .AddSingleton<IInteractionControlFactory<BooleanContainerOptions>, SettingToggleFactory>()
            .AddSingleton<IInteractionControlFactory<ConfigurablePathsContainerOption>, SettingPathsFactory>()

            // Other
            .AddSingleton<InjectedViewLocator>()
            .AddSingleton<CollectionDataProvider>()
            .AddSingleton<ILibraryDataProvider, LocalFileDataProvider>()
            .AddSingleton<ILoadoutDataProvider, LocalFileDataProvider>()
            .AddSingleton<ILibraryDataProvider, ManuallyCreatedArchiveDataProvider>()
            .AddSingleton<ILoadoutDataProvider, ManuallyCreatedArchiveDataProvider>()
            .AddSingleton<ILibraryDataProvider, NexusModsDataProvider>()
            .AddSingleton<ILoadoutDataProvider, NexusModsDataProvider>()
            .AddSingleton<ILibraryDataProvider, ThunderstoreDataProvider>()
            .AddSingleton<ILoadoutDataProvider, ThunderstoreDataProvider>()
            .AddSingleton<ILibraryDataProvider, ModIoDataProvider>()
            .AddSingleton<ILoadoutDataProvider, ModIoDataProvider>()
            // Mod sources as enumerable capabilities (CODE_REVIEW.md §5): source-agnostic consumers
            // resolve GetServices<IModSource>() instead of hardcoding a per-source property, so a new
            // source is one registration here rather than an edit to every consumer.
            .AddSingleton<IModSource, NexusModSource>()
            .AddSingleton<IModSource, ThunderstoreModSource>()
            .AddSingleton<IModSource, ModIoModSource>()
            .AddSingleton<ILoadoutDataProvider, BundledDataProvider>()
            .AddSingleton<ILoadOrderDataProvider, LoadOrderDataProvider>()
            .AddSingleton<IDownloadsDataProvider, DownloadsDataProvider>()
            .AddSingleton<IEventBus, EventBus>()
            .AddSingleton<IAvaloniaInterop, AvaloniaInterop>()
            .AddSingleton<UpdateChecker>()
            .AddFileSystem()
            .AddImagePipelines();
        
        
    }

}
