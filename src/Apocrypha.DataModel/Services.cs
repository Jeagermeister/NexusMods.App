using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Sdk.Settings;
using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.GC;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Sorting;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Abstractions.Serialization.ExpressionGenerator;
using Apocrypha.DataModel.CommandLine.Verbs;
using Apocrypha.DataModel.Diagnostics;
using Apocrypha.DataModel.JsonConverters;
using Apocrypha.DataModel.SchemaVersions;
using Apocrypha.DataModel.Sorting;
using Apocrypha.DataModel.Synchronizer;
using Apocrypha.DataModel.Synchronizer.DbFunctions;
using Apocrypha.DataModel.Undo;
using NexusMods.HyperDuck;
using NexusMods.HyperDuck.Adaptor.Impls.ValueAdaptor;
using NexusMods.MnemonicDB;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Storage.Abstractions;
using NexusMods.MnemonicDB.Storage.RocksDbBackend;
using Apocrypha.Sdk;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Resources;

using IFileSystem = NexusMods.Paths.IFileSystem;

namespace Apocrypha.DataModel;

/// <summary/>
public static class Services
{
    /// <summary>
    /// Adds all services related to the <see cref="DataModel"/> to your dependency
    /// injection container.
    /// </summary>
    public static IServiceCollection AddDataModel(this IServiceCollection coll)
    {
        coll.AddMnemonicDB();
        coll.AddMigrations();

        coll.AddAmbientQueriesSql();
        coll.AddSynchronizerSql();
        coll.AddSingleton<ATableFunction, IntrinsicFiles>();
        coll.AddSingleton<AScalarFunction, FNV1aHashScalar>();
        coll.AddValueAdaptor<ushort, LocationId>(LocationId.From);

        // Settings
        coll.AddSettings<DataModelSettings>();

        coll.AddSingleton<DatomStoreSettings>(sp =>
            {
                var fileSystem = sp.GetRequiredService<IFileSystem>();
                var settingsManager = sp.GetRequiredService<ISettingsManager>();
                var settings = settingsManager.Get<DataModelSettings>();
                if (settings.UseInMemoryDataModel)
                    return DatomStoreSettings.InMemory;
                
                var path = settings.MnemonicDBPath.ToPath(fileSystem);
                if (!path.DirectoryExists())
                    path.CreateDirectory();
                return new DatomStoreSettings
                {
                    Path = settings.MnemonicDBPath.ToPath(fileSystem),
                };
            }
        );
        
        coll.AddSingleton<IStoreBackend>(_ => new Backend());

        coll.AddSingleton<JsonConverter, AbsolutePathConverter>();
        coll.AddSingleton<JsonConverter, RelativePathConverter>();
        coll.AddSingleton<JsonConverter, GamePathConverter>();
        coll.AddSingleton<JsonConverter, DateTimeConverter>();
        coll.AddSingleton<JsonConverter, SizeConverter>();
        coll.AddSingleton<JsonConverter, GameIdConverter>();
        coll.AddSingleton<JsonConverterFactory, OptionalConverterFactory>();
        coll.AddSingleton<JsonConverter, OptionalConverterFactory>();

        // File Store
        coll.AddAllSingleton<IFileStore, NxFileStore>();
        
        // Readonly stream source
        coll.AddSingleton<IReadOnlyStreamSource>(s => s.GetRequiredService<NxFileStore>());
        coll.AddSingleton<IReadOnlyStreamSource, GameFileStreamSource>();
        coll.AddSingleton<IStreamSourceDispatcher, StreamSourceDispatcher>();
        
        coll.AddAllSingleton<IToolManager, ToolManager>();

        // Disk State and Synchronizer
        coll.AddDiskStateEntryModel();
        coll.AddAllSingleton<ISynchronizerService, SynchronizerService>();

        coll.AddSingleton<ITypeFinder>(_ => new AssemblyTypeFinder(typeof(Services).Assembly));
        coll.AddAllSingleton<ISorter, Sorter>();
        
        // Diagnostics
        coll.AddAllSingleton<IDiagnosticManager, DiagnosticManager>();
        coll.AddSettings<DiagnosticSettings>();
        
        // GC
        coll.AddAllSingleton<IGarbageCollectorRunner, GarbageCollectorRunner>();
        
        
        coll.AddPersistedDbResourceModel();
        
        // Undo
        coll.AddSingleton<UndoService>();

        coll.AddSingleton<ILoadoutManager, LoadoutManager>();

        // Verbs
        coll.AddLoadoutManagementVerbs()
            .AddImportExportVerbs()
            .AddToolVerbs();

        return coll;
    }
}
