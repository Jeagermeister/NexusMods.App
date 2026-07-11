using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.GuidedInstallers;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Backend;
using Apocrypha.Games.FOMOD;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk;
using Apocrypha.StandardGameLocators.TestHelpers;

namespace Apocrypha.Games.RedEngine.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddSingleton<IGuidedInstaller, NullGuidedInstaller>()
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<Cyberpunk2077.Cyberpunk2077Game>(new Version("1.61"))
            .AddFomod()
            .AddRedEngineGames()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .Validate();
    }
}

