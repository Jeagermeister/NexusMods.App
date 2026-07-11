using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Abstractions.Serialization.Attributes;
using Apocrypha.App.UI.Resources;
using Apocrypha.App.UI.Settings;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.UI.Sdk.Icons;

namespace Apocrypha.App.UI.Pages.LibraryPage;

[JsonName("NexusMods.App.UI.Pages.Library.LibraryPageContext")]
public record LibraryPageContext : IPageFactoryContext
{
    public required LoadoutId LoadoutId { get; init; }
}

[UsedImplicitly]
public class LibraryPageFactory : APageFactory<ILibraryViewModel, LibraryPageContext>
{
    public LibraryPageFactory(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public static readonly PageFactoryId StaticId = PageFactoryId.From(Guid.Parse("547926e3-56ba-4ed1-912d-d0d7e8b7e287"));
    public override PageFactoryId Id => StaticId;

    public override ILibraryViewModel CreateViewModel(LibraryPageContext context)
    {
        var vm = new LibraryViewModel(
            ServiceProvider.GetRequiredService<IWindowManager>(), 
            ServiceProvider,
            ServiceProvider.GetRequiredService<IGameDomainToGameIdMappingCache>(),
            context.LoadoutId);
        return vm;
    }

    public override IEnumerable<PageDiscoveryDetails?> GetDiscoveryDetails(IWorkspaceContext workspaceContext)
    {
        if (workspaceContext is not LoadoutContext loadoutContext) yield break;

        yield return new PageDiscoveryDetails
        {
            SectionName = "Mods",
            ItemName = Language.LibraryPageTitle,
            Icon = IconValues.LibraryOutline,
            PageData = new PageData
            {
                FactoryId = Id,
                Context = new LibraryPageContext
                {
                    LoadoutId = loadoutContext.LoadoutId,
                },
            },
        };
    }
}
