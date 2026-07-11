using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.NexusModsLibrary.Models;

namespace Apocrypha.Abstractions.NexusModsLibrary;

/// <summary>
/// Extension methods.
/// </summary>
[PublicAPI]
public static class Services
{
    /// <summary>
    /// Extension method.
    /// </summary>
    public static IServiceCollection AddNexusModsLibraryModels(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddNexusModsFileMetadataModel()
            .AddNexusModsModPageMetadataModel()
            .AddNexusModsLibraryItemModel()
            .AddCollectionMetadataModel()
            .AddCollectionRevisionMetadataModel()
            .AddCollectionDownloadModel()
            .AddCollectionDownloadExternalModel()
            .AddCollectionDownloadNexusModsModel()
            .AddCollectionDownloadBundledModel()
            .AddCollectionDownloadRulesModel()
            .AddCollectionCategoryModel()
            .AddUserModel()
            .AddNexusModsCollectionLibraryFileModel();
    }
}
