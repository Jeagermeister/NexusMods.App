using System.Collections.ObjectModel;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.LeftMenu.Items;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.LeftMenu.Downloads;

public class DownloadsLeftMenuDesignViewModel : AViewModel<IDownloadsLeftMenuViewModel>, IDownloadsLeftMenuViewModel
{
    public WorkspaceId WorkspaceId { get; } = WorkspaceId.NewId();
    
    public ILeftMenuItemViewModel LeftMenuItemAllDownloads { get; } = new LeftMenuItemDesignViewModel
    {
        Text = new StringComponent(Language.DownloadsLeftMenu_AllDownloads),
        Icon = IconValues.Download,
    };

    public ReadOnlyObservableCollection<ILeftMenuItemViewModel> LeftMenuItemsPerGameDownloads { get; }

    public DownloadsLeftMenuDesignViewModel()
    {
        var perGameItems = new[]
        {
            new LeftMenuItemDesignViewModel
            {
                Text = new StringComponent(string.Format(Language.DownloadsLeftMenu_GameSpecificDownloads, "Stardew Valley")),
                Icon = IconValues.FolderEditOutline,
            },
            new LeftMenuItemDesignViewModel
            {
                Text = new StringComponent(string.Format(Language.DownloadsLeftMenu_GameSpecificDownloads, "Cyberpunk 2077")),
                Icon = IconValues.FolderEditOutline,
            },
            new LeftMenuItemDesignViewModel
            {
                Text = new StringComponent(string.Format(Language.DownloadsLeftMenu_GameSpecificDownloads, "Skyrim")),
                Icon = IconValues.FolderEditOutline,
            },
        };
        
        LeftMenuItemsPerGameDownloads = new ReadOnlyObservableCollection<ILeftMenuItemViewModel>(
            new ObservableCollection<ILeftMenuItemViewModel>(perGameItems));
    }
}
