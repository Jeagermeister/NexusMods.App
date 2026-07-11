using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.Networking.Thunderstore.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddLogging(builder => builder.AddXunitOutput().SetMinimumLevel(LogLevel.Trace));
    }
}
