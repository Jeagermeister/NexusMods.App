using Avalonia.Platform.Storage;
using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.App.UI.Controls.Settings.SettingEntries.PathsList;

public interface ISettingPathsViewModel : IInteractionControl
{
    ConfigurablePathsContainer ConfigurablePathsContainer { get; }

    R3.ReactiveCommand CommandChangeLocation { get; }
    IStorageProvider? StorageProvider { get; set; }
}
