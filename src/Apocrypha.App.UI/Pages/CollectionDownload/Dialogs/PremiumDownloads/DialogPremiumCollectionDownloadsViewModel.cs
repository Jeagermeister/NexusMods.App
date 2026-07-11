using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.UI.Sdk;
using R3;

namespace Apocrypha.App.UI.Pages.CollectionDownload.Dialogs.PremiumDownloads;

public interface IDialogPremiumCollectionDownloadsViewModel : IViewModelInterface
{
}

public class DialogPremiumCollectionDownloadsViewModel : AViewModel<IDialogPremiumCollectionDownloadsViewModel>, IDialogPremiumCollectionDownloadsViewModel
{

    public DialogPremiumCollectionDownloadsViewModel()
    {
    }
}
