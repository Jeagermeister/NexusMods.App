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

namespace Apocrypha.Games.CreationEngine.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddSingleton<IGuidedInstaller, NullGuidedInstaller>()
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<CreationEngine.SkyrimSE.SkyrimSE>(new Version("1.6.1170"))
            .AddFomod()
            .AddCreationEngine()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .Validate();
    }
}

