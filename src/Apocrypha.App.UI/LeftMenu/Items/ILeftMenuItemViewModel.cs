using System.Reactive;
using Apocrypha.App.UI.Controls;
using Apocrypha.App.UI.Controls.Navigation;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;
using ReactiveUI;

namespace Apocrypha.App.UI.LeftMenu.Items;

public interface ILeftMenuItemViewModel : IViewModelInterface
{
    public StringComponent Text { get; }
    
    public IconValue Icon { get; set; }
    
    public string ToolTipText { get; }
    
    public ReactiveCommand<NavigationInformation, Unit> NavigateCommand { get; }
    
    public bool IsActive { get; }
    
    public bool IsSelected { get; }
    
    public IReadOnlyList<IContextMenuItem> AdditionalContextMenuItems { get; }
}
