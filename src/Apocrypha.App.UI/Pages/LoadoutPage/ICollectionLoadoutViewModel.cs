using System.Reactive;
using Avalonia.Media.Imaging;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.LoadoutPage;

public interface ICollectionLoadoutViewModel : IPageViewModelInterface
{
    LoadoutTreeDataGridAdapter Adapter { get; }

    /// <summary>
    /// Gets whether the collection is local or remote.
    /// </summary>
    bool IsLocalCollection { get; }

    /// <summary>
    /// Gets whether the collection is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Gets whether the collection is enabled.
    /// </summary>
    bool IsCollectionEnabled { get; }
    
    /// <summary>
    /// Gets the number of mods installed in the collection, both required and optional.
    /// </summary>
    int InstalledModsCount { get; }

    /// <summary>
    /// Gets the number of optional mods offered by the collection revision (installed or not).
    /// </summary>
    int OptionalModsCount { get; }

    string Name { get; }
    
    /// <inheritdoc cref="CollectionMetadata.Endorsements"/>
    ulong EndorsementCount { get; }

    /// <inheritdoc cref="CollectionMetadata.TotalDownloads"/>
    ulong TotalDownloads { get; }

    /// <inheritdoc cref="CollectionRevisionMetadata.TotalSize"/>
    Size TotalSize { get; }

    /// <inheritdoc cref="CollectionRevisionMetadata.OverallRating"/>
    Percent OverallRating { get; }
    
    

    RevisionNumber RevisionNumber { get; }

    string AuthorName { get; }

    Bitmap? AuthorAvatar { get; }

    Bitmap? TileImage { get; }

    Bitmap? BackgroundImage { get; }

    R3.ReactiveCommand<R3.Unit> CommandToggle { get; }

    R3.ReactiveCommand<R3.Unit> CommandDeleteCollection { get; }

    R3.ReactiveCommand<R3.Unit> CommandMakeLocalEditableCopy { get; }

    ReactiveCommand<NavigationInformation, Unit> CommandViewCollectionDownloadPage { get; }

    /// <summary>
    /// Opens the collection download page on the Optional tab, where optional mods can be
    /// downloaded and installed.
    /// </summary>
    ReactiveCommand<NavigationInformation, Unit> CommandViewOptionalMods { get; }

    /// <summary>
    /// Whether a newer published revision of this collection exists on Nexus Mods.
    /// </summary>
    R3.BindableReactiveProperty<bool> IsUpdateAvailable { get; }

    /// <summary>
    /// The newest published revision number, if it's newer than the installed one.
    /// </summary>
    R3.BindableReactiveProperty<DynamicData.Kernel.Optional<RevisionNumber>> NewestRevisionNumber { get; }

    /// <summary>
    /// Opens the collection download page for the newest published revision.
    /// </summary>
    R3.ReactiveCommand<R3.Unit> CommandUpdateCollection { get; }
}
