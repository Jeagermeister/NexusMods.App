using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Serialization.Attributes;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.Sdk;
using Apocrypha.UI.Sdk;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.Pages.DebugControls;

[JsonName("DebugControlsPageContext")]
public record DebugControlsPageContext : IPageFactoryContext;

public class DebugControlsPageFactory : APageFactory<IDebugControlsPageViewModel, DebugControlsPageContext>
{
    public static readonly PageFactoryId StaticId = PageFactoryId.From(Guid.Parse("8fb63069-e912-4a10-a46e-3c5048ee5e61"));
    public override PageFactoryId Id => StaticId;

    public DebugControlsPageFactory(IServiceProvider serviceProvider) : base(serviceProvider) { }
    
    public override IDebugControlsPageViewModel CreateViewModel(DebugControlsPageContext context)
    {
        return new DebugControlsPageViewModel(WindowManager, ServiceProvider, ServiceProvider.GetRequiredService<IWindowNotificationService>());
    }

    public override IEnumerable<PageDiscoveryDetails?> GetDiscoveryDetails(IWorkspaceContext workspaceContext)
    {
        if (!ApplicationConstants.IsDebug) return [];

        return
        [
            new PageDiscoveryDetails
            {
                Icon = IconValues.ColorLens,
                ItemName = "Debug Controls",
                SectionName = "Utilities",
                PageData = new PageData
                {
                    FactoryId = StaticId,
                    Context = new DebugControlsPageContext(),
                },
            },
        ];
    }
}
