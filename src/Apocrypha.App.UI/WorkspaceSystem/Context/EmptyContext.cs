using Apocrypha.Abstractions.Serialization.Attributes;

namespace Apocrypha.App.UI.WorkspaceSystem;

[JsonName("NexusMods.App.UI.WorkspaceSystem.EmptyContext")]
public record EmptyContext : IWorkspaceContext
{
    public static readonly IWorkspaceContext Instance = new EmptyContext();

    public bool IsValid(IServiceProvider serviceProvider) => true;
}
