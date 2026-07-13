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

    public static IDialog AddModIoModFromLink()
    {
        return DialogFactory.CreateStandardDialog(
            "Add a Mod From a mod.io Link",
            new StandardDialogParameters()
            {
                Text = "Paste a link to a mod on mod.io. The mod's latest file will be downloaded into your Library.",
                InputLabel = "Mod link",
                InputWatermark = "e.g. https://mod.io/g/baldursgate3/m/some-mod",
            },
            [
                DialogStandardButtons.Cancel,
                new DialogButtonDefinition(
                    "Add mod",
                    ButtonDefinitionId.Accept,
                    ButtonAction.Accept,
                    ButtonStyling.Primary
                ),
            ],
            DialogWindowSize.Small
        );
    }

    public static IDialog SetModIoApiKey()
    {
        return DialogFactory.CreateStandardDialog(
            "Connect to mod.io",
            new StandardDialogParameters()
            {
                Text = """
                       Downloading from mod.io needs a free API key (read-only access).

                       Get one at mod.io/me/access, then paste it here. It's stored locally and only sent to mod.io.
                       """,
                InputLabel = "API key",
                InputWatermark = "paste your mod.io API key",
            },
            [
                DialogStandardButtons.Cancel,
                new DialogButtonDefinition(
                    "Save key",
                    ButtonDefinitionId.Accept,
                    ButtonAction.Accept,
                    ButtonStyling.Primary
                ),
            ],
            DialogWindowSize.Small
        );
    }
}
