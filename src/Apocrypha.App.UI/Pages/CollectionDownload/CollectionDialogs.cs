using Apocrypha.App.UI.Dialog;
using Apocrypha.App.UI.Pages.CollectionDownload.Dialogs.PremiumDownloads;
using Apocrypha.App.UI.Resources;
using Apocrypha.UI.Sdk.Dialog;
using Apocrypha.UI.Sdk.Dialog.Enums;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.Pages.CollectionDownload;

public static class CollectionDialogs
{
    public static IDialog PremiumCollectionDialog()
    {
        var premiumCollectionDownloadsViewModel = new DialogPremiumCollectionDownloadsViewModel();

        return DialogFactory.CreateDialog(Language.DialogPremiumCollection_DialogTitle,
            [
                new DialogButtonDefinition(
                    Language.DialogPremiumCollection_Cancel,
                    ButtonDefinitionId.From("cancel"),
                    ButtonAction.Reject
                ),
                new DialogButtonDefinition(
                    Language.DialogPremiumCollection_UpgradeToPremium,
                    ButtonDefinitionId.From("go-premium"),
                    ButtonAction.Accept,
                    ButtonStyling.Premium,
                    IconValues.Premium
                )
            ],
            premiumCollectionDownloadsViewModel
        );
    }

}
