using Apocrypha.App.UI.Controls.MarkdownRenderer;
using Apocrypha.Sdk.Settings;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.App.UI.Controls.Settings.SettingEntries;

public interface ISettingEntryViewModel : IViewModelInterface
{
    PropertyConfig Config { get; }

    IMarkdownRendererViewModel DescriptionMarkdownRenderer { get; }

    IInteractionControl InteractionControlViewModel { get; }

    IMarkdownRendererViewModel? LinkRenderer { get; }
}
