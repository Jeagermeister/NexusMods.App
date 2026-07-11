using Apocrypha.Sdk.Settings;

namespace Apocrypha.App.UI.Settings;

public record WelcomeSettings : ISettings
{
    public bool HasShownWelcomeMessage { get; set; }

    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder) => settingsBuilder;
}
