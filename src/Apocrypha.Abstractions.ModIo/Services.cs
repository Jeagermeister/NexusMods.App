using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.ModIo.Models;

namespace Apocrypha.Abstractions.ModIo;

/// <summary>
/// Extension methods.
/// </summary>
[PublicAPI]
public static class Services
{
    /// <summary>
    /// Registers the mod.io MnemonicDB models.
    /// </summary>
    public static IServiceCollection AddModIoModels(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddModIoModMetadataModel()
            .AddModIoFileMetadataModel()
            .AddModIoLibraryItemModel();
    }
}
