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
using Apocrypha.StandardGameLocators.TestHelpers.StubbedGames;

namespace Apocrypha.Games.CreationEngine.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddSingleton<IGuidedInstaller, NullGuidedInstaller>()
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<CreationEngine.SkyrimSE.SkyrimSE>(new Version("1.6.1170"))
            // CI runners cannot resolve KnownPath.MyGamesDirectory, so the Creation Engine games
            // fail to locate there; datastore-backed offline tests run against the stubbed game,
            // whose locations are all locator-relative.
            .AddGame<StubbedGame>()
            .AddUniversalGameLocator<StubbedGame>(new Version("0.0.0"))
            .AddFomod()
            .AddCreationEngine()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .Validate();
    }
}

