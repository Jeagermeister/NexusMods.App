using FomodInstaller.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Backend;
using Apocrypha.Games.RedEngine;
using Apocrypha.Games.RedEngine.Cyberpunk2077;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.Games.FOMOD.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddRedEngineGames()
            .AddLoadoutAbstractions()
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.6.659.0"))
            .AddFomod()
            .AddSingleton<ICoreDelegates, MockDelegates>()
            .AddLogging(builder => builder.AddXunitOutput().SetMinimumLevel(LogLevel.Debug))
            .AddGames()
            .AddGameServices()
            .Validate();
    }
}
