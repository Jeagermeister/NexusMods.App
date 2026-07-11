using System.Windows.Input;

namespace Apocrypha.App.UI.Controls.Spine.Buttons.Icon;

public interface IIconButtonViewModel : ISpineItemViewModel
{
    /// <summary>
    /// Name for the tooltip on the button.
    /// </summary>
    public string Name { get; set; }

}
