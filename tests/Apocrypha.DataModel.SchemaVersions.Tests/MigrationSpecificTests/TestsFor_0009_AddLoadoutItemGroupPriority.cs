using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using Apocrypha.Sdk.Loadouts;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.SchemaVersions.Tests.MigrationSpecificTests;

public class TestsFor_0009_AddLoadoutItemGroupPriority(ITestOutputHelper helper) : ALegacyDatabaseTest(helper)
{
    [Fact]
    public async Task Test()
    {
        await using var tempConnection = await ConnectionFor("Migration-8.rocksdb.zip");
        var db = tempConnection.Connection.Db;

        foreach (var loadout in Loadout.All(db))
        {
            var groupIds = LoadoutItemGroup.All(db)
                .Where(group => group.AsLoadoutItem().LoadoutId == loadout.LoadoutId)
                .Select(group => group.LoadoutItemGroupId)
                .ToArray();
            if (groupIds.Length == 0) continue;

            var priorities = LoadoutItemGroupPriority.FindByLoadout(db, loadout.LoadoutId).ToArray();
            priorities.Select(p => p.TargetId).Should().BeEquivalentTo(groupIds,
                "every LoadoutItemGroup in the loadout should have received a backfilled priority");
            priorities.Select(p => p.Priority.Value).Should().OnlyHaveUniqueItems(
                "no two groups in the same loadout should share a priority");
            priorities.Select(p => p.Priority.Value).Should().NotContain(0UL,
                "priority 0 is the sentinel the synchronizer SQL treats as \"no priority assigned\" " +
                "(coalesce(group_priority.Priority, 0) in WinningLeafLoadoutItem) — a real group must never land there");
        }
    }
}
