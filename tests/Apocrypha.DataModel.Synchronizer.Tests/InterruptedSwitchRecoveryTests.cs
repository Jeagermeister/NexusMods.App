using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.Synchronizer.Tests;

/// <summary>
/// A loadout switch writes and deletes files across the whole game folder and only records
/// <see cref="GameInstallMetadata.LastSyncedLoadout"/> once every one of them has landed. Kill the
/// app in between — and a 132 GB switch leaves a wide window — and disk is part-way to the target
/// while the database still names the loadout being switched away from.
///
/// <para>
/// The damage is not to the half-applied target, which is simply re-applied. It is to the
/// <b>outgoing</b> loadout: the next sync sees the target's files as files the user added and
/// ingests them into the outgoing loadout's Overrides, and sees the outgoing files the switch
/// already deleted as files the user deleted and reifies those deletions. The user loses mods from
/// a loadout they never touched. Review finding C-1.
/// </para>
/// </summary>
public class InterruptedSwitchRecoveryTests(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<InterruptedSwitchRecoveryTests>(helper)
{
    private static readonly GamePath FileOnlyInA = new(LocationId.Game, "bin/onlyInA.txt");
    private static readonly GamePath FileOnlyInB = new(LocationId.Game, "bin/onlyInB.txt");

    [Fact]
    public async Task AnInterruptedSwitchDoesNotCorruptTheOutgoingLoadout()
    {
        // Loadout A owns one file and is the applied loadout.
        var loadoutA = await CreateLoadout();
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, [FileOnlyInA.Path], loadoutA, "ModA");
            await tx.Commit();
        }
        Refresh(ref loadoutA);
        loadoutA = await Synchronizer.Synchronize(loadoutA);

        // Loadout B owns a different file and has never been applied.
        var loadoutB = await CreateLoadout();
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, [FileOnlyInB.Path], loadoutB, "ModB");
            await tx.Commit();
        }
        Refresh(ref loadoutB);

        var pathInA = GameInstallation.Locations.ToAbsolutePath(FileOnlyInA);
        var pathInB = GameInstallation.Locations.ToAbsolutePath(FileOnlyInB);
        pathInA.FileExists.Should().BeTrue("loadout A is applied");

        // Simulate a switch to B that was killed half way through RunActions: B's file has been
        // extracted, A's has been deleted, and neither LastSyncedLoadout nor the disk state has been
        // updated because that only happens in the final transaction.
        pathInB.Parent.CreateDirectory();
        await pathInB.WriteAllTextAsync("half-extracted file belonging to loadout B");
        pathInA.Delete();

        using (var tx = Connection.BeginTransaction())
        {
            tx.Add(loadoutA.Installation.Id, GameInstallMetadata.SwitchInProgressLoadout, loadoutB.Id);
            await tx.Commit();
        }

        Refresh(ref loadoutA);
        loadoutA.Installation.LastSyncedLoadout.Id.Should().Be(loadoutA.Id, "the interrupted switch never got to record B");

        // The dangerous operation: synchronizing the loadout that was being switched AWAY from.
        loadoutA = await Synchronizer.Synchronize(loadoutA);

        // B's half-extracted file must not have been adopted by A.
        TargetPathsOf(loadoutA).Should().NotContain(FileOnlyInB,
            "the interrupted switch's own output is not a user edit, so it must never be attributed to A");

        // A's own file must still belong to A, and must not have been reified as a user deletion.
        TargetPathsOf(loadoutA).Should().Contain(FileOnlyInA);
        DeletedPathsOf(loadoutA).Should().NotContain(FileOnlyInA,
            "the switch deleted this file, not the user");

        // A was the requested loadout, so disk ends up matching A once recovery has run.
        pathInA.FileExists.Should().BeTrue();
        pathInB.FileExists.Should().BeFalse();

        // And the window is closed again.
        Refresh(ref loadoutA);
        loadoutA.Installation.Contains(GameInstallMetadata.SwitchInProgressLoadout).Should().BeFalse();
    }

    /// <summary>
    /// Recovery must also work when the user picks up where they left off and asks for the target
    /// again, rather than for the loadout that was being switched away from.
    /// </summary>
    [Fact]
    public async Task AnInterruptedSwitchCompletesWhenTheTargetIsRequestedAgain()
    {
        var loadoutA = await CreateLoadout();
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, [FileOnlyInA.Path], loadoutA, "ModA");
            await tx.Commit();
        }
        Refresh(ref loadoutA);
        loadoutA = await Synchronizer.Synchronize(loadoutA);

        var loadoutB = await CreateLoadout();
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, [FileOnlyInB.Path], loadoutB, "ModB");
            await tx.Commit();
        }
        Refresh(ref loadoutB);

        var pathInA = GameInstallation.Locations.ToAbsolutePath(FileOnlyInA);
        var pathInB = GameInstallation.Locations.ToAbsolutePath(FileOnlyInB);

        pathInB.Parent.CreateDirectory();
        await pathInB.WriteAllTextAsync("half-extracted file belonging to loadout B");
        pathInA.Delete();

        using (var tx = Connection.BeginTransaction())
        {
            tx.Add(loadoutA.Installation.Id, GameInstallMetadata.SwitchInProgressLoadout, loadoutB.Id);
            await tx.Commit();
        }

        Refresh(ref loadoutB);
        loadoutB = await Synchronizer.Synchronize(loadoutB);

        // B is now properly applied, and A kept out of it entirely.
        pathInB.FileExists.Should().BeTrue();
        pathInA.FileExists.Should().BeFalse();

        Refresh(ref loadoutA);
        TargetPathsOf(loadoutA).Should().NotContain(FileOnlyInB);
        DeletedPathsOf(loadoutA).Should().NotContain(FileOnlyInA);

        Refresh(ref loadoutB);
        loadoutB.Installation.LastSyncedLoadout.Id.Should().Be(loadoutB.Id);
        loadoutB.Installation.Contains(GameInstallMetadata.SwitchInProgressLoadout).Should().BeFalse();
    }

    private static GamePath[] TargetPathsOf(Loadout.ReadOnly loadout)
    {
        return LoadoutItem.FindByLoadout(loadout.Db, loadout)
            .OfTypeLoadoutItemWithTargetPath()
            .OfTypeLoadoutFile()
            .Select(static file => (GamePath)file.AsLoadoutItemWithTargetPath().TargetPath)
            .ToArray();
    }

    private static GamePath[] DeletedPathsOf(Loadout.ReadOnly loadout)
    {
        return LoadoutItem.FindByLoadout(loadout.Db, loadout)
            .OfTypeLoadoutItemWithTargetPath()
            .OfTypeDeletedFile()
            .Select(static file => (GamePath)file.AsLoadoutItemWithTargetPath().TargetPath)
            .ToArray();
    }
}
