using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Apocrypha.Backend.FileExtractor;
using Apocrypha.Backend.FileExtractor.Extractors;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Backend.Games;
using Apocrypha.Backend.Games.Locators;
using Apocrypha.Backend.Jobs;
using Apocrypha.Backend.OS;
using Apocrypha.Backend.Process;
using Apocrypha.Backend.RuntimeDependency;
using Apocrypha.FileExtractor;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.FileExtractor;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Jobs;
using Apocrypha.Sdk.Settings;
using Apocrypha.UI.Sdk.Icons;
using Apocrypha.UI.Sdk.Settings;

namespace Apocrypha.Backend;

public static class ServiceExtensions
{
    public static IServiceCollection AddGameLocators(
        this IServiceCollection serviceCollection,
        GameLocatorSettings? settings = null)
    {
        OSInformation.Shared.SwitchPlatform(
            onWindows: () =>
            {
                serviceCollection.AddSingleton<IGameLocator, SteamLocator>();
                serviceCollection.AddSingleton<IGameLocator, GOGLocator>();
                serviceCollection.AddSingleton<IGameLocator, EGSLocator>();

                if (settings?.EnableXboxGamePass ?? false)
                    serviceCollection.AddSingleton<IGameLocator, XboxLocator>();
            },
            onLinux: () =>
            {
                serviceCollection.AddSingleton<IGameLocator>(serviceProvider => new SteamLocator(serviceProvider.GetServices<IGameData>(), serviceProvider.GetRequiredService<ILoggerFactory>(), serviceProvider.GetRequiredService<IFileSystem>(), registry: null));
                serviceCollection.AddSingleton<IGameLocator, HeroicGOGLocator>();

                serviceCollection.AddSingleton<IGameLocator>(serviceProvider =>
                {
                    var locatorFactories = new WinePrefixWrappingLocator.LocatorFactory[]
                    {
                        (provider, loggerFactory, fileSystem, registry) => new GOGLocator(provider.GetServices<IGameData>(), loggerFactory, fileSystem, registry),
                        (provider, loggerFactory, fileSystem, registry) => new EGSLocator(provider.GetServices<IGameData>(), loggerFactory, fileSystem, registry),
                    };

                    return new WinePrefixWrappingLocator(serviceProvider, locatorFactories);
                });
            }
        );

        return serviceCollection;
    }

    public static IServiceCollection AddGameServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddSingleton<IGameLocationsService, GameLocationsService>()
            .AddSingleton<IGameRegistry, GameRegistry>()
            .AddGameInstallMetadataModel()
            .AddSettings<GameLocatorSettings>();
    }

    public static IServiceCollection AddOSInterop(this IServiceCollection serviceCollection, IOSInformation? os = null)
    {
        os ??= OSInformation.Shared;

        serviceCollection = serviceCollection
            .AddSingleton(os)
            .AddSingleton<IProcessRunner, ProcessRunner>();

        serviceCollection = os.MatchPlatform(
            ref serviceCollection,
            onWindows: static (ref IServiceCollection value) => value.AddSingleton<IOSInterop, WindowsInterop>(),
            onLinux: static (ref IServiceCollection value) => value.AddSingleton<IOSInterop, LinuxInterop>()
        );

        return serviceCollection;
    }

    public static IServiceCollection AddRuntimeDependencies(this IServiceCollection serviceCollection, IOSInformation? os = null)
    {
        os ??= OSInformation.Shared;

        serviceCollection = serviceCollection.AddHostedService<RuntimeDependencyChecker>();

        if (os.IsLinux)
        {
            serviceCollection = serviceCollection
                .AddAllSingleton<IRuntimeDependency, XdgSettingsDependency>()
                .AddAllSingleton<IRuntimeDependency, UpdateDesktopDatabaseDependency>();
        }

        return serviceCollection;
    }

    public static IServiceCollection AddSettingsManager(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddSingleton<ISettingsManager, SettingsManager>()
            .AddStorageBackend<JsonStorageBackend>()
            .AddStorageBackend<MnemonicDBStorageBackend>(isDefault: true)
            .AddSingleton(new SectionDescriptor(
                Id: Sections.General,
                Name: "General",
                IconFunc: () => IconValues.Desktop,
                Priority: ushort.MaxValue
            ))
            .AddSingleton(new SectionDescriptor(
                Id: Sections.GameSpecific,
                Name: "Game specific",
                IconFunc: () => IconValues.Game,
                Priority: ushort.MinValue + 3
            ))
            .AddSingleton(new SectionDescriptor(
                Id: Sections.Advanced,
                Name: "Advanced",
                IconFunc: () => IconValues.School,
                Priority: ushort.MinValue + 2
            ))
            .AddSingleton(new SectionDescriptor(
                Id: Sections.DeveloperTools,
                Name: "Developer tools",
                IconFunc: () => IconValues.Code,
                Priority: ushort.MinValue + 1
            ))
            .AddSingleton(new SectionDescriptor(
                Id: Sections.Experimental,
                Name: "Experimental - Not currently supported",
                IconFunc: () => IconValues.WarningAmber,
                Priority: ushort.MinValue,
                Hidden: !ApplicationConstants.IsDebug
            ))
            .AddSettingModel();
    }
    
    /// <summary>
    /// Adds file extraction related services to the provided DI container.
    /// </summary>
    public static IServiceCollection AddFileExtractors(this IServiceCollection coll)
    {
        coll.AddSettings<FileExtractorSettings>();
        coll.AddFileExtractorVerbs();
        coll.AddSingleton<IFileExtractor, FileExtractor.FileExtractor>();
        coll.AddSingleton<IExtractor, SevenZipExtractor>();
        coll.AddSingleton<IExtractor, ManagedZipExtractor>();
        coll.TryAddSingleton<TemporaryFileManager, TemporaryFileManagerEx>();
        return coll;
    }
    
    public static IServiceCollection AddJobMonitor(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddSingleton<IJobMonitor, JobMonitor>();
    }
}
