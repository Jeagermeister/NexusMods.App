using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Settings;
using Apocrypha.Games.RedEngine.Cyberpunk2077;
using Apocrypha.Games.RedEngine.Cyberpunk2077.Models;
using Apocrypha.Games.RedEngine.Cyberpunk2077.SortOrder;

namespace Apocrypha.Games.RedEngine;

public static class Services
{
    public static IServiceCollection AddRedEngineGames(this IServiceCollection services)
    {
        services.AddGame<Cyberpunk2077Game>()
            .AddRedModInfoFileModel()
            .AddRedModSortOrderModel()
            .AddRedModLoadoutGroupModel()
            .AddRedModSortOrderItemModel()
            .AddRedModQueriesSql()
            .AddSingleton<RedModSortOrderVariety, RedModSortOrderVariety>()
            .AddSingleton<ITool, RunCyberpunk2077Game>()
            .AddSingleton<ITool, RedModDeployTool>()
            // Diagnostics
            
            
            .AddSettings<Cyberpunk2077Settings>();
        return services;
    }
}
