using System.Reactive.Disposables;
using Avalonia.ReactiveUI;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.App.UI.Resources;
using ReactiveUI;

namespace Apocrypha.App.UI.Pages.CollectionDownload.Dialogs.PremiumDownloads;

public partial class DialogPremiumCollectionDownloadsView : ReactiveUserControl<IDialogPremiumCollectionDownloadsViewModel>
{
    public DialogPremiumCollectionDownloadsView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
            {
                
            }
        );
    }
}
