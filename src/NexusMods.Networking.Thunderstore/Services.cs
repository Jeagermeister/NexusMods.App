using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Thunderstore;
using NexusMods.Networking.Thunderstore.CLI;
using NexusMods.Sdk.Settings;

namespace NexusMods.Networking.Thunderstore;

/// <summary>
/// Extension methods.
/// </summary>
public static class Services
{
    /// <summary>
    /// Adds Thunderstore as a mod source: the API client, the library facade, the dependency
    /// resolver, the MnemonicDB models, and the CLI verbs.
    /// </summary>
    public static IServiceCollection AddThunderstore(this IServiceCollection services)
    {
        return services
            .AddThunderstoreModels()
            .AddSettings<ThunderstoreSettings>()
            .AddSingleton<IThunderstoreApiClient, ThunderstoreApiClient>()
            .AddSingleton<IThunderstoreLibrary, ThunderstoreLibrary>()
            .AddSingleton<ThunderstoreDependencyResolver>()
            .AddThunderstoreVerbs();
    }
}
