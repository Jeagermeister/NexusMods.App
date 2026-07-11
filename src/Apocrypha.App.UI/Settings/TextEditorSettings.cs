using JetBrains.Annotations;
using Apocrypha.Sdk.Settings;
using TextMateSharp.Grammars;

namespace Apocrypha.App.UI.Settings;

public record TextEditorSettings : ISettings
{
    public ThemeName ThemeName { get; [UsedImplicitly] set; } = ThemeName.Dark;

    public double FontSize { get; [UsedImplicitly] set; } = 14;

    public static ISettingsBuilder Configure(ISettingsBuilder settingsBuilder)
    {
        return settingsBuilder;
    }
}
