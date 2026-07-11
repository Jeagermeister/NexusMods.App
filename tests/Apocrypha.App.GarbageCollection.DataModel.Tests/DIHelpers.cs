using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Backend;
using Apocrypha.Games.TestFramework;
using Apocrypha.StandardGameLocators.TestHelpers;
using Apocrypha.StandardGameLocators.TestHelpers.StubbedGames;

namespace Apocrypha.App.GarbageCollection.DataModel.Tests;

public static class DIHelpers
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddDefaultServicesForTesting()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .AddGames()
            .AddGame<StubbedGame>()
            .AddUniversalGameLocator<StubbedGame>(Version.Parse("0.0.0"));
    }
}
