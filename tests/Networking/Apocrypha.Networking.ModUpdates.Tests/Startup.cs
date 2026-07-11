using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Games.TestFramework;

namespace Apocrypha.Networking.ModUpdates.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container.AddDefaultServicesForTesting();
    }
}

