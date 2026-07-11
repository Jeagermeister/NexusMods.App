using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using NexusMods.Paths;
using Apocrypha.Sdk.Jobs;
using R3;

namespace Apocrypha.App.UI.Pages.LoadoutPage;

public class CollectionLoadoutDesignViewModel : APageViewModel<ICollectionLoadoutViewModel>, ICollectionLoadoutViewModel
{
    public CollectionLoadoutDesignViewModel() : base(new DesignWindowManager()) { }

    public LoadoutTreeDataGridAdapter Adapter { get; } = null!;
    public bool IsCollectionEnabled => true;
    public int InstalledModsCount { get; } = 10;
    public int OptionalModsCount { get; } = 5;
    public string Name => "Vanilla+ [Quality of Life]";
    
    public ulong EndorsementCount => 123456;
    public ulong TotalDownloads => 123456789;
    public Size TotalSize => Size.From(1234567890);
    public Percent OverallRating => Percent.CreateClamped(0.75);
    public RevisionNumber RevisionNumber { get; } = RevisionNumber.From(6);
    public string AuthorName => "Lowtonotolerance";
    public Bitmap? AuthorAvatar => new(AssetLoader.Open(new Uri("avares://Apocrypha.App.UI/Assets/DesignTime/avatar.webp")));
    public Bitmap TileImage { get; } = new(AssetLoader.Open(new Uri("avares://Apocrypha.App.UI/Assets/DesignTime/collection_tile_image.png")));
    public Bitmap BackgroundImage { get; } = new(AssetLoader.Open(new Uri("avares://Apocrypha.App.UI/Assets/DesignTime/header-background.webp")));
    public ReactiveCommand<Unit> CommandToggle { get; } = new ReactiveCommand();
    public ReactiveCommand<Unit> CommandDeleteCollection { get; } = new ReactiveCommand();
    public ReactiveCommand<Unit> CommandMakeLocalEditableCopy { get; } = new ReactiveCommand();
    public BindableReactiveProperty<bool> IsUpdateAvailable { get; } = new(value: false);
    public BindableReactiveProperty<DynamicData.Kernel.Optional<RevisionNumber>> NewestRevisionNumber { get; } = new(value: DynamicData.Kernel.Optional<RevisionNumber>.None);
    public ReactiveCommand<Unit> CommandUpdateCollection { get; } = new ReactiveCommand();

    public ReactiveUI.ReactiveCommand<NavigationInformation, System.Reactive.Unit> CommandViewCollectionDownloadPage { get; }
        = ReactiveUI.ReactiveCommand.Create<NavigationInformation, System.Reactive.Unit>(_ => System.Reactive.Unit.Default);
    public ReactiveUI.ReactiveCommand<NavigationInformation, System.Reactive.Unit> CommandViewOptionalMods { get; }
        = ReactiveUI.ReactiveCommand.Create<NavigationInformation, System.Reactive.Unit>(_ => System.Reactive.Unit.Default);
    public bool IsLocalCollection { get; } = false;
    public bool IsReadOnly { get; } = true;
}
