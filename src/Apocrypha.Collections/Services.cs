using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Collections;

public static class Services
{
    public static IServiceCollection AddNexusModsCollections(this IServiceCollection services)
    {
        return services
            .AddManagedCollectionLoadoutGroupModel()
            .AddNexusCollectionLoadoutGroupModel()
            .AddDirectDownloadLibraryFileModel()
            .AddNexusCollectionBundledLoadoutGroupModel()
            .AddNexusCollectionItemLoadoutGroupModel()
            .AddNexusCollectionReplicatedLoadoutGroupModel()
            .AddCollectionVerbs()
            .AddSingleton<CollectionDownloader>()
            .AddSettings<DownloadSettings>();
    }
}
