using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.CreationEngine.Models;
using Apocrypha.Games.CreationEngine.SortOrder;

namespace Apocrypha.Games.CreationEngine;

public static class Services
{
    public static IServiceCollection AddCreationEngine(this IServiceCollection services)
    {
        services.AddGame<SkyrimSE.SkyrimSE>();
        services.AddSingleton<ITool>(s => RunGameViaScriptExtenderTool<SkyrimSE.SkyrimSE>.Create(s, KnownPaths.SKSE64Loader));

        services.AddGame<Fallout4.Fallout4>();
        services.AddSingleton<ITool>(s => RunGameViaScriptExtenderTool<Fallout4.Fallout4>.Create(s, KnownPaths.F4SELoader));

        services
            .AddSingleton<PluginSortOrderVariety>()
            .AddSingleton<ICollectionLoadOrderSeeder, CollectionPluginOrderSeeder>()
            .AddPluginSortOrderItemModel()
            .AddPluginSortOrderQueriesSql();

        return services;
    }
}
