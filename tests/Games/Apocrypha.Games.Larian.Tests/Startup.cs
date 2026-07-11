using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.GuidedInstallers;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Backend;
using Apocrypha.Games.Larian.BaldursGate3;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk;
using Apocrypha.StandardGameLocators.TestHelpers;

namespace Apocrypha.Games.Larian.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddSingleton<IGuidedInstaller, NullGuidedInstaller>()
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<Larian.BaldursGate3.BaldursGate3>(new Version("1.61"))
            .AddBaldursGate3()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .Validate();
    }
}
