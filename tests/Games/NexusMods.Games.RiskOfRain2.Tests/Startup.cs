using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Thunderstore;
using NexusMods.Backend;
using NexusMods.Games.TestFramework;
using NexusMods.Sdk;
using NexusMods.StandardGameLocators.TestHelpers;

namespace NexusMods.Games.RiskOfRain2.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<RiskOfRain2Game>(new Version("1.3.9"))
            .AddRiskOfRain2()
            .AddThunderstoreModels()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .Validate();
    }
}
