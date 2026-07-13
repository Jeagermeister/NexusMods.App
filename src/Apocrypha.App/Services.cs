using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Serialization;
using Apocrypha.App.Commandline;
using Apocrypha.App.UI;
using Apocrypha.App.UI.Settings;
using Apocrypha.Backend;
using Apocrypha.Backend.Games.Locators;
using Apocrypha.CLI;
using Apocrypha.Collections;
using Apocrypha.CrossPlatform;
using Apocrypha.DataModel;
using Apocrypha.DataModel.JsonConverters;
using Apocrypha.Games.AdvancedInstaller;
using Apocrypha.Games.AdvancedInstaller.UI;
using Apocrypha.Games.FileHashes;
using Apocrypha.Games.FOMOD;
using Apocrypha.Games.FOMOD.UI;
using Apocrypha.Games.Generic;
using Apocrypha.Library;
using Apocrypha.Networking.EpicGameStore;
using Apocrypha.Networking.GitHub;
using Apocrypha.Networking.GOG;
using Apocrypha.Networking.HttpDownloader;
using Apocrypha.Networking.NexusWebApi;
using Apocrypha.Networking.Steam;
using Apocrypha.Networking.ModIo;
using Apocrypha.Networking.Thunderstore;
using NexusMods.Paths;
using Apocrypha.ProxyConsole;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.ProxyConsole;
using Apocrypha.Sdk.Settings;
using Apocrypha.SingleProcess;

namespace Apocrypha.App;

public static class Services
{
    public static IServiceCollection AddApp(
        this IServiceCollection services,
        bool addStandardGameLocators = true,
        StartupMode? startupMode = null,
        ExperimentalSettings? experimentalSettings = null,
        GameLocatorSettings? gameLocatorSettings = null)
    {
        services.Configure<HostOptions>(options =>
        {
            // Sequential execution can lead to long startup times depending on number of hostedServices.
            options.ServicesStartConcurrently = true;
            // If executed sequentially, one service taking a long time can trigger the timeout,
            // preventing StopAsync of other services from being called. 
            options.ServicesStopConcurrently = true;
        });
        startupMode ??= new StartupMode();
        if (startupMode.RunAsMain)
        {
            services
                .AddEpicGameStore()
                .AddSingleton<TimeProvider>(_ => TimeProvider.System)
                .AddDataModel()
                .AddLibrary()
                .AddLibraryModels()
                .AddJobMonitor()
                .AddNexusModsCollections()

                .AddSettings<LoggingSettings>()
                .AddSettings<ExperimentalSettings>()
                .AddDefaultRenderers()
                .AddDefaultParsers()

                .AddSingleton<CommandLineConfigurator>()
                .AddCLI()
                .AddUI()
                .AddSettingsManager()
                .AddSingleton<App>()
                .AddGuidedInstallerUi()
                .AddAdvancedInstaller()
                .AddAdvancedInstallerUi()
                .AddFileExtractors()
                .AddSerializationAbstractions()
                .AddSupportedGames()
                .AddOSInterop()
                .AddRuntimeDependencies()
                .AddGames()
                .AddGameServices()
                .AddGenericGameSupport()
                .AddLoadoutAbstractions()
                .AddFomod()
                .AddNexusWebApi()
                .AddHttpDownloader()
                // .AddAdvancedHttpDownloader()
                .AddFileSystem()
                .AddCleanupVerbs()
                .AddStatusVerbs()
                .AddSteamCli()
                .AddThunderstore()
                .AddModIo()
                .AddGOG()
                .AddFileHashes()
                .AddGitHubApi();

            if (!startupMode.IsAvaloniaDesigner)
                services.AddSingleProcess(Mode.Main);

            if (addStandardGameLocators)
                services.AddGameLocators(settings: gameLocatorSettings);
        }
        else
        {
            services
                .AddSingleton<TimeProvider>(_ => TimeProvider.System)
                .AddFileSystem()
                .AddOSInterop()
                .AddRuntimeDependencies()
                .AddDefaultRenderers()
                .AddSettingsManager()
                .AddSingleton<JsonConverter, AbsolutePathConverter>()
                .AddSerializationAbstractions()
                .AddSettings<LoggingSettings>();

            if (!startupMode.IsAvaloniaDesigner)
                services.AddSingleProcess(Mode.Client);
        }

        return services;
    }
    
    private static IServiceCollection AddSupportedGames(this IServiceCollection services)
    {
        Games.RedEngine.Services.AddRedEngineGames(services);
        Games.StardewValley.Services.AddStardewValley(services);
        Games.Larian.BaldursGate3.Services.AddBaldursGate3(services);
        Games.CreationEngine.Services.AddCreationEngine(services);
        Games.MountAndBlade2Bannerlord.Services.AddMountAndBlade2Bannerlord(services);
        Games.BepInEx.Services.AddBepInExGames(services);
        return services;
    }
}
