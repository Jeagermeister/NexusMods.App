using Apocrypha.Sdk.Settings;
using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.App.UI.Controls.Settings.SettingEntries;

public interface ISettingComboBoxViewModel : IInteractionControl
{
    string[] DisplayItems { get; }

    int SelectedItemIndex { get; set; }
}
