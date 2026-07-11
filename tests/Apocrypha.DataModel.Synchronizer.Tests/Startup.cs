using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Backend;
using Apocrypha.CrossPlatform;
using Apocrypha.Games.Generic;
using NexusMods.Paths;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.DataModel.Synchronizer.Tests;

public class Startup
{
    /// <summary>
    /// Why are Cyberpunk tests in a generic DataModel project, well it's, so we can test something that's close to real-world data. 
    /// </summary>
    /// <param name="container"></param>
    public void ConfigureServices(IServiceCollection container)
    {
        container
            .AddSingleton<TimeProvider>(_ => TimeProvider.System)
            .AddSettings<LoggingSettings>()
            .AddSettingsManager()
            .AddFileSystem()
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddGenericGameSupport();
    }
}

