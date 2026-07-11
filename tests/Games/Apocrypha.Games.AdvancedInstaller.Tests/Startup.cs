using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk;

namespace Apocrypha.Games.AdvancedInstaller.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddDefaultServicesForTesting()
            .AddLogging(builder => builder.AddXUnit())
            .Validate();
    }
}
