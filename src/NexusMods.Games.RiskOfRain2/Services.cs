using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Games.RiskOfRain2.Emitters;
using NexusMods.Games.RiskOfRain2.Installers;
using NexusMods.Games.RiskOfRain2.Models;

namespace NexusMods.Games.RiskOfRain2;

/// <summary>
/// Extension methods.
/// </summary>
public static class Services
{
    /// <summary>
    /// Adds Risk of Rain 2 (the Thunderstore/BepInEx pilot game) to the DI container.
    /// </summary>
    public static IServiceCollection AddRiskOfRain2(this IServiceCollection services)
    {
        return services
            .AddGame<RiskOfRain2Game>()
            .AddSingleton<BepInExPackInstaller>()
            .AddSingleton<BepInExPluginInstaller>()
            .AddSingleton<MissingBepInExEmitter>()
            .AddBepInExLoadoutItemModel()
            .AddBepInExPluginLoadoutItemModel();
    }
}
