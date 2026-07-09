using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;

namespace NexusMods.Games.RiskOfRain2;

/// <summary>
/// Extension methods.
/// </summary>
public static class Services
{
    /// <summary>
    /// Adds Risk of Rain 2 (the Thunderstore/BepInEx pilot game) to the DI container.
    /// Requires <c>AddBepInExGames()</c> (NexusMods.Games.BepInEx) to be registered as well —
    /// the shared installers, emitter, and loadout-item models live there since Phase 2.
    /// </summary>
    public static IServiceCollection AddRiskOfRain2(this IServiceCollection services)
    {
        return services
            .AddGame<RiskOfRain2Game>()
            .AddSingleton<ITool, RunRiskOfRain2Game>();
    }
}
