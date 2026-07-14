using System.Diagnostics;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using NexusMods.HyperDuck;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.DataModel.SchemaVersions.Migrations;

public class _0009_AddLoadoutItemGroupPriority : ITransactionalMigration
{
    public static (MigrationId Id, string Name) IdAndName => MigrationId.ParseNameAndId(nameof(_0009_AddLoadoutItemGroupPriority));

    private static EntityId[] Query(IDb db, LoadoutId loadoutId)
    {
        // All LoadoutItemGroups in this loadout that don't yet have a conflict priority, so we can
        // backfill one for each. The original referenced a nonexistent `loadouts.ItemGroupEnabledState`
        // macro; this uses only the generated MDB_* table macros, which are always available at
        // migration time. A LoadoutItemGroup shares its entity id with its LoadoutItem base, so we
        // join on Id to read the Loadout reference (a group's own macro doesn't carry it).
        return db.Connection.Query<EntityId>($"""
                                                SELECT
                                                  item_group.Id
                                                FROM
                                                  MDB_LOADOUTITEMGROUP (Db => {db}) item_group
                                                  JOIN MDB_LOADOUTITEM (Db => {db}) item ON item.Id = item_group.Id
                                                  LEFT JOIN MDB_LOADOUTITEMGROUPPRIORITY (Db => {db}) group_priority ON item_group.Id = group_priority.Target
                                                WHERE
                                                  group_priority.Target IS NULL
                                                  AND item.Loadout = {loadoutId.Value}
                                                ORDER BY item_group.Id;
                                                """
        ).ToArray();
    }

    private Dictionary<LoadoutId, EntityId[]> _loadouts = [];

    public Task Prepare(IDb db)
    {
        var loadouts = Loadout.All(db);
        _loadouts = loadouts.ToDictionary(loadout => loadout.LoadoutId, loadout => Query(db, loadout));
        return Task.CompletedTask;
    }

    public void Migrate(ITransaction tx, IDb db)
    {
        foreach (var kv in _loadouts)
        {
            var groups = kv.Value;
            Debug.Assert(groups.Length >= 0);
            for (ulong i = 0; i < (ulong)groups.Length; i++)
            {
                var loadoutItemGroup = LoadoutItemGroupId.From(groups[i]);
                var priority = new LoadoutItemGroupPriority.New(tx)
                {
                    TargetId = loadoutItemGroup,
                    Priority = ConflictPriority.From(i),
                    LoadoutId = kv.Key
                };
            }
        }
    }
}
