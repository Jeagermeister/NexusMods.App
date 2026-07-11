using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Backend;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk;

namespace Apocrypha.Games.Generic.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddDefaultServicesForTesting()
            .AddLogging(builder => builder.AddXUnit())
            .AddGames()
            .AddGameServices()
            .Validate();
    }
}
