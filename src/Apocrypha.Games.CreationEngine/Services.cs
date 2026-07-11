using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;

namespace Apocrypha.Games.CreationEngine;

public static class Services
{
    public static IServiceCollection AddCreationEngine(this IServiceCollection services)
    {
        services.AddGame<SkyrimSE.SkyrimSE>();
        services.AddSingleton<ITool>(s => RunGameViaScriptExtenderTool<SkyrimSE.SkyrimSE>.Create(s, KnownPaths.SKSE64Loader));

        services.AddGame<Fallout4.Fallout4>();
        services.AddSingleton<ITool>(s => RunGameViaScriptExtenderTool<Fallout4.Fallout4>.Create(s, KnownPaths.F4SELoader));

        return services;
    }
}
