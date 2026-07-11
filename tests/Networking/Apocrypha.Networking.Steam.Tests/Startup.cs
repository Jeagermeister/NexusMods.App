using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Networking.HttpDownloader;
using NexusMods.Paths;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.Networking.Steam.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddHttpDownloader()
            .AddSteam()
            .AddLoggingAuthenticationHandler()
            .AddLocalAuthFileStorage()
            .AddFileSystem()
            .AddLogging(builder => builder.AddXunitOutput().SetMinimumLevel(LogLevel.Trace));
    }
}

