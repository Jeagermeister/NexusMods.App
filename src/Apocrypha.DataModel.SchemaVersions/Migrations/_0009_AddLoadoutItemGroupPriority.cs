using System.Diagnostics;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.DataModel.SchemaVersions.Migrations;

public class _0009_AddLoadoutItemGroupPriority : ITransactionalMigration
{
    public static (MigrationId Id, string Name) IdAndName => MigrationId.ParseNameAndId(nameof(_0009_AddLoadoutItemGroupPriority));

    private readonly IConnection _connection;

    public _0009_AddLoadoutItemGroupPriority(IConnection connection)
    {
        _connection = connection;
    }

    private Dictionary<LoadoutId, LoadoutItemGroupId[]> _missingGroups = [];
    private Dictionary<LoadoutId, ulong> _maxExistingPriority = [];

    public async Task Prepare(IDb db)
    {
        // A database that predates the priority feature entirely has never had this attribute
        // registered in its own local schema — MnemonicDB only allows reads/writes against an
        // attribute once the connected database has seen it at least once, which the app's normal
        // "always been part of a new install's baseline schema" bootstrap path never had to handle.
        // UpdateSchema declares it for this specific database if it isn't already. Re-declaring an
        // attribute that's already registered is NOT safe to do unconditionally — it corrupted
        // unrelated attribute ids in an already-current test database when called unguarded — so
        // this only calls it when the attribute is genuinely missing from this database's own
        // cache. Doing so advances the connection's live db, so everything below re-reads it fresh
        // via _connection.Db instead of the (now potentially stale) `db` parameter.
        //
        // This also intentionally avoids the raw MDB_LOADOUTITEMGROUPPRIORITY SQL macro the first
        // version of this migration used (see git history / the original bug this replaced, which
        // referenced a nonexistent `loadouts.ItemGroupEnabledState` macro): that macro isn't
        // reliably queryable immediately after UpdateSchema registers the attribute, throwing a
        // DuckDB catalog error. The generated model accessors below hit the datom store directly
        // and aren't affected by that.
        if (!db.AttributeCache.Has(LoadoutItemGroupPriority.Target.Id))
        {
            await _connection.UpdateSchema([
                LoadoutItemGroupPriority.Loadout,
                LoadoutItemGroupPriority.Target,
                LoadoutItemGroupPriority.Priority,
            ]);
        }

        var freshDb = _connection.Db;
        var allGroups = LoadoutItemGroup.All(freshDb).ToArray();

        _missingGroups = [];
        _maxExistingPriority = [];
        foreach (var loadout in Loadout.All(freshDb))
        {
            var groupIds = allGroups
                .Where(group => group.AsLoadoutItem().LoadoutId == loadout.LoadoutId)
                .Select(group => group.LoadoutItemGroupId)
                .ToArray();
            if (groupIds.Length == 0) continue;

            var existing = LoadoutItemGroupPriority.FindByLoadout(freshDb, loadout.LoadoutId).ToArray();
            var alreadyPrioritized = existing.Select(priority => priority.TargetId).ToHashSet();

            // Sorted for a stable, deterministic backfill order across runs.
            _missingGroups[loadout.LoadoutId] = groupIds
                .Where(id => !alreadyPrioritized.Contains(id))
                .OrderBy(id => id.Value)
                .ToArray();

            // The live install path (GetNextPriority) assigns 1-indexed priorities to any group
            // added after the priority feature shipped, so a loadout reaching this migration can
            // already have some groups prioritized and some not — backfilling blindly from 0 would
            // collide with those. And the synchronizer's own winning-file SQL treats a missing
            // priority row as 0 (coalesce(group_priority.Priority, 0) in WinningLeafLoadoutItem),
            // so priority 0 itself is never a safe value to hand to a real group — it reads as
            // "unprioritized" downstream. Starting each loadout's backfill at (current max + 1)
            // avoids both problems at once.
            _maxExistingPriority[loadout.LoadoutId] = existing.Length == 0 ? 0UL : existing.Max(priority => priority.Priority.Value);
        }
    }

    public void Migrate(ITransaction tx, IDb db)
    {
        foreach (var (loadoutId, groups) in _missingGroups)
        {
            Debug.Assert(groups.Length >= 0);
            var nextPriority = _maxExistingPriority.GetValueOrDefault(loadoutId) + 1;
            for (var i = 0; i < groups.Length; i++)
            {
                _ = new LoadoutItemGroupPriority.New(tx)
                {
                    TargetId = groups[i],
                    Priority = ConflictPriority.From(nextPriority + (ulong)i),
                    LoadoutId = loadoutId,
                };
            }
        }
    }
}
