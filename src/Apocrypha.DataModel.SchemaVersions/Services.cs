using Microsoft.Extensions.DependencyInjection;
using Apocrypha.DataModel.SchemaVersions.Migrations;

namespace Apocrypha.DataModel.SchemaVersions;

public static class Services
{
    public static IServiceCollection AddMigrations(this IServiceCollection services)
    {
        services.AddSchemaVersionModel();
        services.AddMigrationLogItemModel();
        services.AddTransient<MigrationService>();

        // Migrations go here:
        return services
            .AddMigration<_0001_ConvertTimestamps>()
            .AddMigration<_0002_NexusCollectionItem>()
            .AddMigration<_0003_FixDuplicates>()
            .AddMigration<_0004_RemoveGameFiles>()
            .AddMigration<_0005_MD5Hashes>()
            .AddMigration<_0006_DirectDownload>()
            .AddMigration<_0007_AddSortOrderParentEntity>()
            .AddMigration<_0008_AddCollectionId>();

        // NOTE(review Tier 1 #6): _0009_AddLoadoutItemGroupPriority backfills file-conflict
        // priorities for loadouts created before that feature. Without it, legacy loadouts tie every
        // conflict at priority 0 and are invisible to the conflicts UI. Its query has been fixed — it
        // previously referenced a nonexistent `loadouts.ItemGroupEnabledState` macro — but a migration
        // runs against real user databases at startup, and this change has NOT been executed against a
        // legacy database. Before registering it, add a legacy-DB test alongside the other
        // MigrationSpecificTests: migrate a pre-priority (Migration-8) snapshot and assert every
        // LoadoutItemGroup receives a sequential priority. Then append to the chain above:
        //     .AddMigration<_0009_AddLoadoutItemGroupPriority>()
    }

    /// <summary>
    /// Add a migration to the DI container
    /// </summary>
    public static IServiceCollection AddMigration<T>(this IServiceCollection services) where T : IMigration
    {
        return services.AddSingleton<MigrationDefinition>(_ => new MigrationDefinition(T.IdAndName.Id, T.IdAndName.Name, typeof(T)))
            // Transient so that migrations can store data locally
            .AddTransient(typeof(T));
    }
}
