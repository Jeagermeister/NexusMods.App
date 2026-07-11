using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Serialization;
using Apocrypha.Backend;
using Apocrypha.CrossPlatform;
using Apocrypha.DataModel;
using Apocrypha.FileExtractor;
using Apocrypha.Games.FileHashes;
using Apocrypha.Library;
using Apocrypha.Networking.HttpDownloader;
using Apocrypha.Networking.HttpDownloader.Tests;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Settings;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.Networking.NexusWebApi.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddGameServices()
            .AddSerializationAbstractions()
            .AddFileSystem()
            .AddSettingsManager()
            .AddHttpDownloader()
            .AddSingleton<TemporaryFileManager>()
            .AddSingleton<LocalHttpServer>()
            .AddNexusWebApi(true)
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddSettings<LoggingSettings>()
            .AddLoadoutAbstractions()
            .AddJobMonitor()
            .AddLibrary()
            .AddLibraryModels()
            .AddFileExtractors()
            .AddFileHashes()
            .AddDataModel() // this is required because we're also using NMA integration
            .OverrideSettingsForTests<DataModelSettings>(settings => settings with
            {
                UseInMemoryDataModel = true,
            })
            .AddLogging(builder => builder.AddXunitOutput()
                .SetMinimumLevel(LogLevel.Debug))
            .Validate();
    }
}

