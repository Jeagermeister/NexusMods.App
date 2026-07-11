using JetBrains.Annotations;
using Apocrypha.Abstractions.Serialization.Attributes;

namespace Apocrypha.App.UI.WorkspaceSystem;

[PublicAPI]
public sealed record WindowData
{
    public required WorkspaceId? ActiveWorkspaceId { get; init; }

    public required WorkspaceData[] Workspaces { get; init; }
}
