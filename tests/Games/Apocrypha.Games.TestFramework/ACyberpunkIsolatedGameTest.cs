using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Backend;
using Apocrypha.CrossPlatform;
using Apocrypha.Games.Generic;
using Apocrypha.Games.RedEngine;
using Apocrypha.Games.RedEngine.Cyberpunk2077;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace Apocrypha.Games.TestFramework;

/// <summary>
/// A override for the <see cref="AIsolatedGameTest{TGame}"/> for the <see cref="Cyberpunk2077Game"/>.
/// </summary>
public class ACyberpunkIsolatedGameTest<TTest>(ITestOutputHelper helper) : AIsolatedGameTest<TTest, Cyberpunk2077Game>(helper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddGenericGameSupport()
            .AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.61"))
            .AddRedEngineGames();
    }
}
