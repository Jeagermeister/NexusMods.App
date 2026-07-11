using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Serialization;
using Apocrypha.App.UI;
using Apocrypha.Backend;
using Apocrypha.CrossPlatform;
using Apocrypha.DataModel;
using Apocrypha.FileExtractor;
using Apocrypha.Games.FileHashes;
using Apocrypha.Library;
using Apocrypha.Networking.GOG;
using Apocrypha.Networking.HttpDownloader;
using Apocrypha.Networking.HttpDownloader.Tests;
using Apocrypha.Networking.NexusWebApi;
using Apocrypha.Networking.Thunderstore;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Settings;
using Apocrypha.SingleProcess;
using Apocrypha.StandardGameLocators;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.CLI.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        const KnownPath baseKnownPath = KnownPath.EntryDirectory;
        var baseDirectory = $"Apocrypha.UI.Tests.Tests-{Guid.NewGuid()}";

        services
                .AddSingleton<CommandLineConfigurator>()
                .AddFileSystem()
                .AddSettingsManager()
                .AddDataModel()
                .AddLibrary()
                .AddLibraryModels()
                .AddJobMonitor()
                .OverrideSettingsForTests<DataModelSettings>(settings => settings with
                {
                    UseInMemoryDataModel = true,
                    MnemonicDBPath = new ConfigurablePath(baseKnownPath, $"{baseDirectory}/MnemonicDB.rocksdb"),
                    ArchiveLocations = [
                        new ConfigurablePath(baseKnownPath, $"{baseDirectory}/Archives"),
                    ],
                })
                .AddFileExtractors()
                .AddFileHashes()
                .AddCLI()
                .AddHttpDownloader()
                .AddGOG()
                .AddThunderstore()
                .AddSingleton<Apocrypha.Sdk.EventBus.IEventBus, EventBus>()
                .AddNexusWebApi(true)
                .AddLoadoutAbstractions()
                .AddSerializationAbstractions()
                .AddGames()
                .AddGameServices()
                .AddOSInterop()
                .AddRuntimeDependencies()
                .AddSettings<LoggingSettings>()
                .AddLogging(builder => builder.AddXunitOutput().SetMinimumLevel(LogLevel.Trace))
                .AddSingleton<LocalHttpServer>()
                .AddLogging(builder => builder.AddXUnit())
                .Validate();
    }
}

