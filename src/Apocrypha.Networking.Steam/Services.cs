using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Steam;
using Apocrypha.Abstractions.Steam.Values;
using Apocrypha.Networking.Steam.CLI;
using Apocrypha.Sdk.ProxyConsole;

namespace Apocrypha.Networking.Steam;

public static class Services
{
    /// <summary>
    /// Add the steam store DI systems to the container
    /// </summary>
    public static IServiceCollection AddSteam(this IServiceCollection services)
    {
        services.AddSingleton<ISteamSession, Session>();
        // Linux fork: login-free local recognition helpers (read on-disk depot manifests + hash the
        // installed game files) used by the `steam local-index`/`steam recognize-game` verbs and the
        // in-app recognition action.
        services.AddSingleton<Local.LocalManifestReader>();
        services.AddSingleton<Local.LocalFileHasher>();
        services.AddSingleton<Apocrypha.Abstractions.Games.FileHashes.ILocalGameVersionRecognizer, Local.LocalGameVersionRecognizer>();
        return services;
    }
    
    /// <summary>
    /// Adds a logging authentication handler to the DI container
    /// </summary>
    public static IServiceCollection AddLoggingAuthenticationHandler(this IServiceCollection services)
    {
        services.AddSingleton<IAuthInterventionHandler, LoggingAuthInterventionHandler>();
        return services;
    }
    
    /// <summary>
    /// Adds auth storage to the DI container that stores the auth data in the app directory
    /// </summary>
    public static IServiceCollection AddLocalAuthFileStorage(this IServiceCollection services)
    {
        services.AddSingleton<IAuthStorage, AppDirectoryAuthStorage>();
        return services;
    }
    
    public static IServiceCollection AddSteamCli(this IServiceCollection services)
    {
        services.AddOptionParser<AppId>(s =>
            {
                if (uint.TryParse(s, out var parsed))
                    return (AppId.From(parsed), null);
                return (default(AppId), "Invalid AppId");
            }
        );
        services.AddSteam();
        services.AddSingleton<IAuthInterventionHandler, RenderingAuthenticationHandler>();
        services.AddLocalAuthFileStorage();
        services.AddSteamVerbs();
        return services;
    }
    
}
