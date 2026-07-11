using System.Text.Json.Serialization;
using FomodInstaller.Interface;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games.FileHashes;
using Apocrypha.Abstractions.Games.FileHashes.Models;
using Apocrypha.Abstractions.Serialization;
using Apocrypha.Backend;
using Apocrypha.Collections;
using Apocrypha.CrossPlatform;
using Apocrypha.DataModel;
using Apocrypha.FileExtractor;
using Apocrypha.Games.FileHashes;
using Apocrypha.Games.Generic;
using Apocrypha.Library;
using Apocrypha.Networking.HttpDownloader;
using Apocrypha.Networking.NexusWebApi;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Settings;
using Apocrypha.StandardGameLocators;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.Games.TestFramework;

/// <summary>
/// Helper functions for dealing with dependency injection.
/// </summary>
[PublicAPI]
public static class DependencyInjectionHelper
{
    /// <summary>
    /// Adds the following default services to the provided <see cref="IServiceCollection"/> for testing:
    /// <list type="bullet">
    ///     <item>Logging via <see cref="LoggingServiceCollectionExtensions.AddLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/></item>
    ///     <item><see cref="IFileSystem"/> via <see cref="Paths.Services.AddFileSystem"/></item>
    ///     <item><see cref="TemporaryFileManager"/> singleton</item>
    ///     <item><see cref="HttpClient"/> singleton</item>
    ///     <item>Nexus Web API via <see cref="Networking.NexusWebApi.Services.AddNexusWebApi"/></item>
    ///     <item>All services related to the <see cref="Apocrypha.DataModel"/> via <see cref="DataModel.Services.AddDataModel"/></item>
    ///     <item>File extraction services via <see cref="Apocrypha.FileExtractor.Services.AddFileExtractors"/></item>
    /// </list>
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <returns></returns>
    public static IServiceCollection AddDefaultServicesForTesting(this IServiceCollection serviceCollection, AbsolutePath prefix = default(AbsolutePath), bool stubbedFileHashService = true)
    {
        const KnownPath baseKnownPath = KnownPath.EntryDirectory;
        var baseDirectory = $"DataModel.{Guid.NewGuid()}";
        prefix = prefix == default(AbsolutePath) ? FileSystem.Shared
            .GetKnownPath(KnownPath.EntryDirectory)
            .Combine($"Apocrypha.Games.TestFramework-{Guid.NewGuid()}") : prefix;

        serviceCollection
            .AddLogging(builder => builder.AddXunitOutput().SetMinimumLevel(LogLevel.Debug))
            .AddSerializationAbstractions()
            .AddFileSystem()
            .AddSingleton<ICoreDelegates, MockDelegates>()
            .AddSingleton<TemporaryFileManager>(_ => new TemporaryFileManager(FileSystem.Shared, prefix))
            .AddNexusWebApi(true)
            .AddNexusModsCollections()
            .AddOSInterop()
            .AddRuntimeDependencies()
            .AddGenericGameSupport()
            .AddSettings<LoggingSettings>()
            .AddHttpDownloader()
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
            .OverrideSettingsForTests<FileHashesServiceSettings>(settings => settings with
            {
                HashDatabaseLocation = new ConfigurablePath(baseKnownPath, $"{baseDirectory}/FileHashes"),
            })
            .AddSettingsManager()
            .AddFileExtractors();
        
        if (stubbedFileHashService)
            serviceCollection
                .AddPathHashRelationModel()
                .AddVersionDefinitionModel()
                .AddGogBuildModel()
                .AddGogDepotModel()
                .AddGogManifestModel()
                .AddSteamManifestModel()
                .AddEpicGameStoreBuildModel()
                .AddFileHashesQueriesSql()
                .AddHashRelationModel()
                .AddSingleton<IFileHashesService, StubbedFileHasherService>();
        else 
            serviceCollection
                .AddFileHashes();

        return serviceCollection;
    }

    /// <summary>
    /// Finds an implementation <typeparamref name="TImplementation"/> of
    /// <typeparamref name="TInterface"/> inside the provided DI container.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <typeparam name="TImplementation"></typeparam>
    /// <typeparam name="TInterface"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception">Thrown when the implementation hasn't been registered in the DI container.</exception>
    public static TImplementation FindImplementationInContainer<TImplementation, TInterface>(this IServiceProvider serviceProvider)
    {
        var service = serviceProvider.GetService(typeof(TImplementation));
        if (service is TImplementation implementation) return implementation;

        var implementations = serviceProvider.GetServices(typeof(TInterface));
        if (implementations is null)
            throw new Exception($"{typeof(TImplementation)} is not registered in the DI container!");

        var validImplementations = implementations.OfType<TImplementation>();
        var validImplementation = validImplementations.FirstOrDefault();

        if (validImplementation is null)
            throw new Exception($"{typeof(TImplementation)} is not registered in the DI container!");

        return validImplementation;
    }
}
