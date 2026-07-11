using Apocrypha.Abstractions.Serialization.Attributes;

namespace Apocrypha.App.UI.WorkspaceSystem;

[JsonName("NexusMods.App.UI.WorkspaceSystem.DownloadsContext")]
public record DownloadsContext : IWorkspaceContext
{
    public bool IsValid(IServiceProvider serviceProvider) => true;
}
