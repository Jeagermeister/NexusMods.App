using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using Apocrypha.Backend;
using Apocrypha.Games.RedEngine;
using Apocrypha.Games.RedEngine.Cyberpunk2077;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.DataModel.Tests;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection container)
    {
        ConfigureTestedServices(container);
        container.AddLogging(builder => builder.AddXunitOutput());
    }
    
    public static void ConfigureTestedServices(IServiceCollection container)
    {
        AddServices(container);
    }
    
    public static IServiceCollection AddServices(IServiceCollection container)
    {
        const KnownPath baseKnownPath = KnownPath.EntryDirectory;
        var baseDirectory = $"Apocrypha.DataModel.Tests-{Guid.NewGuid()}";

        var prefix = FileSystem.Shared
            .GetKnownPath(baseKnownPath)
            .Combine(baseDirectory);

        return container
            .AddGameServices()
            .AddLoadoutItemGroupPriorityModel()
            .AddSortOrderItemModel()
            .AddDefaultServicesForTesting()
            .AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.61"))
            .AddRedEngineGames()
            .AddLoadoutAbstractions()
            .Validate();
    }
}

