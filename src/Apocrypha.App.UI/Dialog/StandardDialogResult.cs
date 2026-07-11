using Apocrypha.UI.Sdk.Dialog;

namespace Apocrypha.App.UI.Dialog;

public record StandardDialogResult
{
    public ButtonDefinitionId ButtonId { get; set; } = ButtonDefinitionId.None;
    public string InputText { get; set; } = string.Empty;
}
