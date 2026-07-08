using NexusMods.Abstractions.Games;

namespace NexusMods.Games.RiskOfRain2;

/// <summary>
/// Launches Risk of Rain 2 (via <c>steam://run</c> for Steam installs, so the user's
/// Steam launch options — e.g. the Proton WINEDLLOVERRIDES for BepInEx — apply).
/// </summary>
public class RunRiskOfRain2Game(IServiceProvider provider, RiskOfRain2Game game) : RunGameTool<RiskOfRain2Game>(provider, game);
