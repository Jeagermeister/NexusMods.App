using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModSources;
using Apocrypha.Networking.ModIo.CLI;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Networking.ModIo;

/// <summary>
/// Extension methods.
/// </summary>
public static class Services
{
    /// <summary>
    /// Adds mod.io as a mod source: the API client, the library facade, the MnemonicDB
    /// models, the CLI verbs, and the <see cref="IModSource"/> adapter.
    /// </summary>
    public static IServiceCollection AddModIo(this IServiceCollection services)
    {
        return services
            .AddModIoModels()
            .AddSettings<ModIoSettings>()
            .AddSingleton<IModIoApiClient, ModIoApiClient>()
            .AddSingleton<IModIoLibrary, ModIoLibrary>()
            .AddSingleton<IModSource, ModIoModSource>()
            .AddModIoVerbs();
    }
}
