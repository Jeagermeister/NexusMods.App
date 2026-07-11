using JetBrains.Annotations;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.App.UI.Settings;

public class UpdaterSettings : ISettings
{
    public Version VersionToSkip { get; [UsedImplicitly] set; } = new(0, 0, 0);

    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder;
    }
}
