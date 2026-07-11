using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.App.UI.Controls.Settings.SettingEntries;

public interface ISettingToggleViewModel : IInteractionControl
{
    BooleanContainer BooleanContainer { get; }
}
