using Apocrypha.App.UI.Dialog;
using Apocrypha.App.UI.Dialog.Enums;
using Apocrypha.UI.Sdk.Dialog;
using Apocrypha.UI.Sdk.Dialog.Enums;

namespace Apocrypha.App.UI.Pages.LibraryPage;

public static class LibraryDialogs
{
    public static IDialog AddCollectionFromLink()
    {
        return DialogFactory.CreateStandardDialog(
            "Add a Collection From a Link",
            new StandardDialogParameters()
            {
                Text = "Paste a link to a Nexus Mods collection, like one a friend shared with you. Unlisted collection links work too.",
                InputLabel = "Collection link",
                InputWatermark = "e.g. https://www.nexusmods.com/games/stardewvalley/collections/tckf0m",
            },
            [
                DialogStandardButtons.Cancel,
                new DialogButtonDefinition(
                    "Add collection",
                    ButtonDefinitionId.Accept,
                    ButtonAction.Accept,
                    ButtonStyling.Primary
                ),
            ],
            DialogWindowSize.Small
        );
    }
}
