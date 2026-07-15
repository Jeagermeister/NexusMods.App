using FluentAssertions;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using Apocrypha.DataModel.SchemaVersions.Migrations;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk.Loadouts;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.SchemaVersions.Tests.MigrationSpecificTests;

/// <summary>
/// A real user's database can reach schema version 8 having already run live code that assigns
/// priorities via the 1-indexed <c>GetNextPriority</c> path (any mod installed after the priority
/// feature shipped gets one), while groups added before that feature existed still have none. This
/// constructs exactly that mixed state and drives the migration directly — the legacy-snapshot test
/// alongside this one can't exercise it, since none of the committed fixtures happen to contain it.
/// </summary>
public class TestsFor_0009_BackfillDoesNotCollideWithLivePriorities(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<TestsFor_0009_BackfillDoesNotCollideWithLivePriorities>(helper)
{
    [Fact]
    public async Task Test()
    {
        await LoadoutManager.ManageInstallation(GameInstallation);
        var loadout = await CreateLoadout();
        LoadoutId loadoutId = loadout;

        EntityId livePriorityGroupId;
        EntityId legacyGroupAId;
        EntityId legacyGroupBId;
        using (var tx = Connection.BeginTransaction())
        {
            var liveGroup = new LoadoutItemGroup.New(tx, out var liveGroupTempId)
            {
                IsGroup = true,
                LoadoutItem = new LoadoutItem.New(tx, liveGroupTempId) { Name = "live-priority-group", LoadoutId = loadoutId },
            };
            _ = new LoadoutItemGroupPriority.New(tx)
            {
                LoadoutId = loadoutId,
                TargetId = liveGroup.LoadoutItemGroupId,
                Priority = ConflictPriority.From(1),
            };

            var legacyA = new LoadoutItemGroup.New(tx, out var legacyATempId)
            {
                IsGroup = true,
                LoadoutItem = new LoadoutItem.New(tx, legacyATempId) { Name = "legacy-group-a", LoadoutId = loadoutId },
            };
            var legacyB = new LoadoutItemGroup.New(tx, out var legacyBTempId)
            {
                IsGroup = true,
                LoadoutItem = new LoadoutItem.New(tx, legacyBTempId) { Name = "legacy-group-b", LoadoutId = loadoutId },
            };

            var result = await tx.Commit();
            livePriorityGroupId = result[liveGroupTempId];
            legacyGroupAId = result[legacyATempId];
            legacyGroupBId = result[legacyBTempId];
        }

        var migration = new _0009_AddLoadoutItemGroupPriority(Connection);
        await migration.Prepare(Connection.Db);
        using (var tx = Connection.BeginTransaction())
        {
            migration.Migrate(tx, Connection.Db);
            await tx.Commit();
        }

        // ManageInstallation/CreateLoadout create some scaffolding groups of their own (e.g. an
        // Overrides group), so the loadout ends up with more than just the three constructed above
        // — that's fine and part of the point: those scaffold groups are themselves unprioritized
        // legacy-shaped data, and a correct migration must backfill them without colliding too.
        var priorities = LoadoutItemGroupPriority.FindByLoadout(Connection.Db, loadoutId).ToArray();
        var byTarget = priorities.ToDictionary(p => p.TargetId.Value, p => p.Priority.Value);
        byTarget.Should().ContainKey(livePriorityGroupId);
        byTarget.Should().ContainKey(legacyGroupAId);
        byTarget.Should().ContainKey(legacyGroupBId);
        byTarget[livePriorityGroupId].Should().Be(1UL, "the live-assigned priority must be untouched by the backfill");

        priorities.Select(p => p.Priority.Value).Should().NotContain(0UL,
            "priority 0 is the sentinel the synchronizer SQL treats as \"no priority assigned\" " +
            "(coalesce(group_priority.Priority, 0) in WinningLeafLoadoutItem) — a real group must never land there");
        priorities.Select(p => p.Priority.Value).Should().OnlyHaveUniqueItems(
            "no two groups in the same loadout may share a priority — that's the exact bug this migration exists to fix");
    }
}
