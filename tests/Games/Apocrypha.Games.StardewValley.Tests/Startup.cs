using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Backend;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.StandardGameLocators.TestHelpers;

namespace Apocrypha.Games.StardewValley.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        var gameFiles = new Dictionary<RelativePath, byte[]>
        {
            { "Stardew Valley.deps.json", "{}"u8.ToArray() }
        };

        container
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<StardewValley>(new Version(1, 0), gameFiles)
            .AddStardewValley()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .AddLoadoutAbstractions()
            .Validate();
    }
}
