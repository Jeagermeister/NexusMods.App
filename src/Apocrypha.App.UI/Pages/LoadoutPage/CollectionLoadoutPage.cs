using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary.Models;
using Apocrypha.Abstractions.Serialization.Attributes;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using Apocrypha.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.App.UI.Pages.LoadoutPage;

[JsonName("NexusMods.App.UI.Pages.Library.CollectionLoadoutPageContext")]
public record CollectionLoadoutPageContext : IPageFactoryContext
{
    public required LoadoutId LoadoutId { get; init; }

    public required CollectionGroupId GroupId { get; init; }

    public Optional<CollectionRevisionMetadataId> RevisionId { get; init; }
}

[UsedImplicitly]
public class CollectionLoadoutPageFactory : APageFactory<ICollectionLoadoutViewModel, CollectionLoadoutPageContext>
{
    private readonly IConnection _connection;

    public CollectionLoadoutPageFactory(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _connection = serviceProvider.GetRequiredService<IConnection>();
    }

    public static readonly PageFactoryId StaticId = PageFactoryId.From(Guid.Parse("cd8b1f11-1d6d-4430-bcbf-33b6001ee743"));
    public override PageFactoryId Id => StaticId;

    public override ICollectionLoadoutViewModel CreateViewModel(CollectionLoadoutPageContext context)
    {
        if (!context.RevisionId.HasValue)
        {
            var group = NexusCollectionLoadoutGroup.Load(_connection.Db, context.GroupId);
            if (group.IsValid())
            {
                context = context with
                {
                    RevisionId = group.RevisionId,
                };
            }
        }

        var vm = new CollectionLoadoutViewModel(ServiceProvider.GetRequiredService<IWindowManager>(), ServiceProvider, context);
        return vm;
    }
}
