using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.LeftMenu;

public interface ILeftMenuViewModel : IViewModelInterface
{
    /// <summary>
    /// The Id of the workspace this left menu is attached to.
    /// </summary>
    public WorkspaceId WorkspaceId { get; }
}
