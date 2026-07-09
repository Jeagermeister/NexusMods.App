using NexusMods.Games.BepInEx;

namespace NexusMods.Games.RiskOfRain2;

/// <summary>
/// Launches Risk of Rain 2. The Proton <c>user.reg</c> winhttp override lives in the shared
/// family base (<see cref="ABepInExRunGameTool{T}"/>) since Phase 2.
/// </summary>
public class RunRiskOfRain2Game : ABepInExRunGameTool<RiskOfRain2Game>
{
    public RunRiskOfRain2Game(IServiceProvider provider, RiskOfRain2Game game) : base(provider, game)
    {
    }
}
