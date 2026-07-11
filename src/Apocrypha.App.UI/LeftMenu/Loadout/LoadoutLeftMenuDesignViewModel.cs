using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.LeftMenu.Items;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using R3;

namespace Apocrypha.App.UI.LeftMenu.Loadout;

public class LoadoutLeftMenuDesignViewModel : AViewModel<ILoadoutLeftMenuViewModel>, ILoadoutLeftMenuViewModel
{
    public ReadOnlyObservableCollection<ILeftMenuItemViewModel> LeftMenuCollectionItems { get; }
    public bool HasSingleCollection { get; } = true;
    public WorkspaceId WorkspaceId { get; } = new();
    public IApplyControlViewModel ApplyControlViewModel { get; } = new ApplyControlDesignViewModel();
    
    public ILeftMenuItemViewModel LeftMenuItemLibrary { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.LibraryPageTitle),
        Icon = IconValues.LibraryOutline,
    };
    public ILeftMenuItemViewModel LeftMenuItemLoadout { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.LoadoutView_Title_Installed_Mods_Default),
        Icon = IconValues.FormatAlignJustify,
    };

    public ILeftMenuItemViewModel LeftMenuItemNewCollection { get; } = null!;

    public ILeftMenuItemViewModel LeftMenuItemHealthCheck { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.LoadoutLeftMenuViewModel_LoadoutLeftMenuViewModel_Diagnostics),
        Icon = IconValues.Cardiology,
    };
    
    public ILeftMenuItemViewModel LeftMenuItemExternalChanges { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.LoadoutLeftMenuViewModel_External_Changes),
        Icon = IconValues.Cog,
    };

    public LoadoutLeftMenuDesignViewModel()
    {
        LeftMenuCollectionItems = new ReadOnlyObservableCollection<ILeftMenuItemViewModel>([
                
                new LeftMenuItemDesignViewModel()
                {
                    Text = new StringComponent("My Collection"),
                    Icon = IconValues.CollectionsOutline,
                    IsToggleVisible = true,
                },
                
                new LeftMenuItemDesignViewModel()
                {
                    Text = new StringComponent("Stardew Valley Very Expanded"),
                    Icon = IconValues.CollectionsOutline,
                    IsToggleVisible = true,
                },
            ]
        );
    }
}
