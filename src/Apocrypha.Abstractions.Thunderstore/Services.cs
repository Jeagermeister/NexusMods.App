using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Thunderstore.Models;

namespace Apocrypha.Abstractions.Thunderstore;

/// <summary>
/// Extension methods.
/// </summary>
[PublicAPI]
public static class Services
{
    /// <summary>
    /// Registers the Thunderstore MnemonicDB models.
    /// </summary>
    public static IServiceCollection AddThunderstoreModels(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddThunderstorePackageMetadataModel()
            .AddThunderstoreVersionMetadataModel()
            .AddThunderstoreLibraryItemModel();
    }
}
