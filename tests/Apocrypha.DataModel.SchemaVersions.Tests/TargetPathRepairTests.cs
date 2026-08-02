using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.DataModel.SchemaVersions.Migrations;
using Apocrypha.Sdk.Loadouts;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.SchemaVersions.Tests;

/// <summary>
/// <see cref="_0010_FixCollectionTargetPaths"/>: rows whose <c>TargetPath.Item1</c> carries the
/// file's own entity id (the InstallCollectionDownloadJob replicated/bundled-install bug) are
/// rewritten to the loadout id the <c>LoadoutItem.Loadout</c> attribute records.
/// </summary>
public class TargetPathRepairTests(ITestOutputHelper helper) : ALegacyDatabaseTest(helper)
{
    [Fact]
    public async Task RepairsSelfReferencingTargetPathsAndIsIdempotent()
    {
        // Any recorded database with real loadout files serves; migrations up to and including
        // _0010 have already run by the time ConnectionFor returns.
        await using var tempConnection = await ConnectionFor("SDV.2_5_2025.rocksdb.zip");
        var connection = tempConnection.Connection;

        var victim = LoadoutItemWithTargetPath.All(connection.Db)
            .First(item => item.AsLoadoutItem().LoadoutId.Value == item.TargetPath.Item1);
        var loadoutId = victim.AsLoadoutItem().LoadoutId;

        // Reproduce the corruption exactly as the install job wrote it: Item1 = the file's own id
        using (var tx = connection.BeginTransaction())
        {
            tx.Add(victim.Id, LoadoutItemWithTargetPath.TargetPath, (victim.Id, victim.TargetPath.Item2, victim.TargetPath.Item3));
            await tx.Commit();
        }
        LoadoutItemWithTargetPath.Load(connection.Db, victim.Id).TargetPath.Item1
            .Should().Be(victim.Id, "the corruption must actually be present for this test to test anything");

        var migration = new _0010_FixCollectionTargetPaths();
        await migration.Prepare(connection.Db);
        using (var tx = connection.BeginTransaction())
        {
            migration.Migrate(tx, connection.Db);
            await tx.Commit();
        }

        var repaired = LoadoutItemWithTargetPath.Load(connection.Db, victim.Id);
        repaired.TargetPath.Item1.Should().Be(loadoutId.Value, "the loadout attribute is the source of truth");
        repaired.TargetPath.Item2.Should().Be(victim.TargetPath.Item2, "only the loadout element is rewritten");
        repaired.TargetPath.Item3.Should().Be(victim.TargetPath.Item3, "only the loadout element is rewritten");

        // Idempotent: nothing left to repair
        var second = new _0010_FixCollectionTargetPaths();
        await second.Prepare(connection.Db);
        using (var tx = connection.BeginTransaction())
        {
            second.Migrate(tx, connection.Db);
            var result = await tx.Commit();
            // An empty repair set produces a transaction with no datoms beyond the tx entity
            result.Db.RecentlyAdded.Count.Should().BeLessThanOrEqualTo(1, "a clean database must not be rewritten");
        }
    }
}
