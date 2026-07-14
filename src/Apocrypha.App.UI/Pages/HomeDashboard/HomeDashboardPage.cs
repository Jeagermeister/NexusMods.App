using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Serialization.Attributes;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.App.UI.Pages.HomeDashboard;

[JsonName("Apocrypha.App.UI.Pages.HomeDashboardPageContext")]
public record HomeDashboardPageContext : IPageFactoryContext;

[UsedImplicitly]
public class HomeDashboardPageFactory : APageFactory<IHomeDashboardViewModel, HomeDashboardPageContext>
{
    public HomeDashboardPageFactory(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public static readonly PageFactoryId StaticId = PageFactoryId.From(Guid.Parse("f3f5b6b0-3f8a-4b0a-9a6e-2e6f0f3a5c11"));
    public override PageFactoryId Id => StaticId;

    public override IHomeDashboardViewModel CreateViewModel(HomeDashboardPageContext context)
    {
        return ServiceProvider.GetRequiredService<IHomeDashboardViewModel>();
    }
}
