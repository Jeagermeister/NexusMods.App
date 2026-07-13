using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.Pages.LibraryPage.Collections;
using Apocrypha.App.UI.WorkspaceSystem;
using R3;

namespace Apocrypha.App.UI.Pages.LibraryPage;

public record InstallationTarget(CollectionGroupId Id, string Name);

public interface ILibraryViewModel : IPageViewModelInterface
{
    LibraryTreeDataGridAdapter Adapter { get; }
    ReadOnlyObservableCollection<ICollectionCardViewModel> Collections { get; }

    ReadOnlyObservableCollection<InstallationTarget> InstallationTargets { get; }
    InstallationTarget? SelectedInstallationTarget { get; set; }

    string EmptyLibrarySubtitleText { get; }

    public ReactiveCommand<Unit> UpdateAllCommand { get; }
    public ReactiveCommand<Unit> RefreshUpdatesCommand { get; }

    ReactiveCommand<Unit> InstallSelectedItemsCommand { get; }
    ReactiveCommand<Unit> InstallSelectedItemsWithAdvancedInstallerCommand { get; }
    ReactiveCommand<Unit> UpdateSelectedItemsCommand { get; }
    ReactiveCommand<Unit> UpdateAndKeepOldSelectedItemsCommand { get; }
    ReactiveCommand<Unit> RemoveSelectedItemsCommand { get; }
    ReactiveCommand<Unit> DeselectItemsCommand { get; }

    /// <summary>
    /// Scans the Library for byte-identical duplicate downloads and offers to remove the
    /// redundant copies (loadout-linked copies are always kept).
    /// </summary>
    ReactiveCommand<Unit> RemoveDuplicatesCommand { get; }

    public int SelectionCount { get; } 
    public int UpdatableSelectionCount { get; }
    public bool HasAnyUpdatesAvailable { get; }
    public bool IsUpdatingAll { get; }
    ReactiveCommand<Unit> OpenFilePickerCommand { get; }
    ReactiveCommand<Unit> OpenNexusModsCommand { get; }
    ReactiveCommand<Unit> OpenNexusModsCollectionsCommand { get; }
    ReactiveCommand<Unit> OpenThunderstoreCommand { get; }
    ReactiveCommand<Unit> OpenModIoCommand { get; }
    ReactiveCommand<Unit> AddCollectionFromLinkCommand { get; }
    ReactiveCommand<Unit> AddModIoModFromLinkCommand { get; }

    /// <summary>
    /// Which mod sources the loadout's game actually has (DESIGN-app-layout.md §5):
    /// the "get mods" entries show a row per available source, not a hardcoded Nexus one.
    /// </summary>
    public bool HasNexusModsSource { get; }
    public bool HasThunderstoreSource { get; }
    public bool HasModIoSource { get; }
    
    IStorageProvider? StorageProvider { get; set; }
}
