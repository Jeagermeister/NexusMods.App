using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.Synchronizer.Tests;

/// <summary>
/// Two mods declaring the same target path with different casing must resolve to ONE winner,
/// chosen by group priority.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RelativePath"/> compares case-insensitively, so <c>Data/Shared.dds</c> and
/// <c>data/shared.dds</c> are one path everywhere in the app — including the
/// <c>Dictionary&lt;GamePath, SyncNode&gt;</c> that <c>BuildSyncTree</c> builds. The winning-files
/// SQL used to group case-sensitively, so it emitted both as winners and the C# loop saw a
/// duplicate for a single key. On the real Fallout 4 loadout that produced <b>570 "Duplicate file"
/// warnings per sync tree</b>, and the survivor was whichever row the scan reached first — group
/// priority, the thing that is supposed to decide conflicts, never got a say.
/// </para>
/// <para>
/// It was also worse than a wrong winner: the duplicate branch was guarded by a
/// <c>Debug.Assert</c>, so a datastore in this state could not boot a Debug build at all (the
/// startup should-sync check runs through here). Only Release limped past by logging.
/// </para>
/// </remarks>
public class CaseVariantPathConflictTests(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<CaseVariantPathConflictTests>(helper)
{
    [Fact]
    public async Task CaseVariantTargetPaths_ResolveToASingleWinner_ChosenByPriority()
    {
        await LoadoutManager.ManageInstallation(GameInstallation);
        var loadout = await CreateLoadout();
        LoadoutId loadoutId = loadout;

        // Same file, two mods, two spellings. The loser deliberately carries the casing that sorts
        // first, so a scan-order winner would pick it and the assertion below would fail.
        var loserPath = new GamePath(LocationId.Game, "Data/Shared.dds");
        var winnerPath = new GamePath(LocationId.Game, "data/shared.dds");

        Hash winnerHash;
        Hash loserHash;

        using (var tx = Connection.BeginTransaction())
        {
            var loserGroup = AddPrioritisedGroup(tx, loadoutId, "low-priority-mod", priority: 1);
            var winnerGroup = AddPrioritisedGroup(tx, loadoutId, "high-priority-mod", priority: 2);

            AddFile(tx, loadoutId, loserGroup, loserPath, "loser", out loserHash, out _);
            AddFile(tx, loadoutId, winnerGroup, winnerPath, "winner", out winnerHash, out _);

            await tx.Commit();
        }

        // Distinct content is what makes the winner identifiable at all.
        winnerHash.Should().NotBe(loserHash);

        loadout = loadout.Rebase();
        var syncTree = Synchronizer.BuildSyncTree(
            latestDiskState: Array.Empty<PathPartPair>(),
            previousDiskState: Array.Empty<PathPartPair>(),
            loadout: loadout
        );

        // GamePath folds case, so both spellings hit the same key either way. What the fold buys is
        // that only one row ever arrives for it, and that it is the right one.
        var matches = syncTree
            .Where(pair => pair.Key.LocationId == LocationId.Game
                           && pair.Key.Path.Equals(winnerPath.Path))
            .ToArray();

        matches.Should().ContainSingle("case-variant spellings of one path collapse to a single sync node");
        matches[0].Value.Loadout.Hash.Should().Be(
            winnerHash,
            "the higher-priority group wins the conflict, and a casing difference must not let the lower-priority file take the path instead"
        );
    }

    /// <summary>
    /// Creates an empty loadout item group carrying an explicit conflict priority.
    /// </summary>
    private static LoadoutItemGroupId AddPrioritisedGroup(ITransaction tx, LoadoutId loadoutId, string name, ulong priority)
    {
        var group = new LoadoutItemGroup.New(tx, out var groupId)
        {
            IsGroup = true,
            LoadoutItem = new LoadoutItem.New(tx, groupId)
            {
                Name = name,
                LoadoutId = loadoutId,
            },
        };

        _ = new LoadoutItemGroupPriority.New(tx)
        {
            LoadoutId = loadoutId,
            TargetId = group.LoadoutItemGroupId,
            Priority = ConflictPriority.From(priority),
        };

        return group.LoadoutItemGroupId;
    }
}
