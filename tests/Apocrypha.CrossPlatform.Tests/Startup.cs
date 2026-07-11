using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Sdk.Settings;
using Apocrypha.Backend;
using NexusMods.Paths;
using Xunit.DependencyInjection;

namespace Apocrypha.CrossPlatform.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddSingleton<TimeProvider>(_ => TimeProvider.System)
            .AddSettingsManager()
            .AddSettings<LoggingSettings>()
            .AddFileSystem()
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddSkippableFactSupport()
            .AddLogging(builder => builder.AddXUnit());
    }
}

