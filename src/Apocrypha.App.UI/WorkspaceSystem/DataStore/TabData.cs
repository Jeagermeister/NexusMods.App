using JetBrains.Annotations;

namespace Apocrypha.App.UI.WorkspaceSystem;

[PublicAPI]
public sealed record TabData
{
    public required PanelTabId Id { get; init; }

    public required PageData PageData { get; init; }
}
