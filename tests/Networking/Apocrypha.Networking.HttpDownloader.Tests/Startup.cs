using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk;

namespace Apocrypha.Networking.HttpDownloader.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection container)
    {
        var prefix = FileSystem.Shared
            .GetKnownPath(KnownPath.EntryDirectory)
            .Combine($"Apocrypha.Networking.HttpDownloader.Tests-{Guid.NewGuid()}");

        container
            .AddDefaultServicesForTesting(prefix)
            .AddSingleton<LocalHttpServer>()
            .AddLogging(builder => builder.AddXUnit())
            .Validate();
    }
}

