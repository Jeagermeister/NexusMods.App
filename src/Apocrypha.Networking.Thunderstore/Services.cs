using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.Networking.Thunderstore.CLI;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Networking.Thunderstore;

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
