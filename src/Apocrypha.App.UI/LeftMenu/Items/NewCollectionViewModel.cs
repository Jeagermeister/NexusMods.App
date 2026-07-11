using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.App.UI.Dialog;
using Apocrypha.App.UI.Dialog.Enums;
using Apocrypha.App.UI.Pages.LoadoutPage;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.Collections;
using Apocrypha.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk.Dialog;
using ReactiveUI;

namespace Apocrypha.App.UI.LeftMenu.Items;

public class NewCollectionViewModel : LeftMenuItemViewModel
{
    public NewCollectionViewModel(
        IServiceProvider serviceProvider,
        LoadoutId loadoutId,
        IWorkspaceController workspaceController,
        WorkspaceId workspaceId) : base(workspaceController, workspaceId, null!)
    {
        Text = new StringComponent(value: "New Collection");
        Icon = IconValues.Add;

        var connection = serviceProvider.GetRequiredService<IConnection>();

        NavigateCommand = ReactiveCommand.CreateFromTask<NavigationInformation>(async (navigationInfo, _) =>
        {
            var dialog = LoadoutDialogs.CreateCollection();
            var windowManager = serviceProvider.GetRequiredService<IWindowManager>();
            var result = await windowManager.ShowDialog(dialog, DialogWindowType.Modal);
            if (result.ButtonId != ButtonDefinitionId.Accept) return;

            var collectionGroup = await CollectionCreator.CreateNewCollectionGroup(connection, loadoutId, newName: result.InputText);

            var pageData = new PageData
            {
                FactoryId = LoadoutPageFactory.StaticId,
                Context = new LoadoutPageContext
                {
                    LoadoutId = loadoutId,
                    GroupScope = collectionGroup.CollectionGroupId,
                },
            };

            var behavior = workspaceController.GetOpenPageBehavior(pageData, navigationInfo);
            workspaceController.OpenPage(workspaceId, pageData, behavior);
        });
    }
}
