using System.Collections.Concurrent;
using System.Reactive;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;

using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Games.TestFramework;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using NexusMods.Paths;
using Apocrypha.Sdk.FileStore;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.IO;
using Apocrypha.Sdk.Loadouts;
using OneOf;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.Synchronizer.Tests;

public class GeneralLoadoutManagementTests(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<GeneralLoadoutManagementTests>(helper)
{

    [Fact]
    public async Task SynchronizerIntegrationTests()
    {
        var sb = new StringBuilder();
        
        var originalFileGamePath = new GamePath(LocationId.Game, "bin/originalGameFile.txt");
        var originalFileFullPath = GameInstallation.Locations.ToAbsolutePath(originalFileGamePath);
        originalFileFullPath.Parent.CreateDirectory();
        await originalFileFullPath.WriteAllTextAsync("Hello World!");

        await LoadoutManager.ManageInstallation(GameInstallation);
        await Synchronizer.ReindexState(GameInstallation);

        LogDiskState(sb, "## 1 - Initial State",
            """
            The initial state of the game folder should contain the game files as they were created by the game store. No loadout has been created yet.
            """);
        var loadoutA = await CreateLoadout();
        var loadoutAState = await SynchronizerService.StatusForLoadout(loadoutA);
        var subADisposable = loadoutAState.SubscribeSafe(new AnonymousObserver<LoadoutSynchronizerState>(_ => { }, onError: exception => sb.AppendLine($"Loadout A State Error: {exception}")));

        LogDiskState(sb, "## 2 - Loadout Created (A) - Synced",
            """
            A new loadout has been created and has been synchronized, so the 'Last Synced State' should be set to match the new loadout.
            """, [loadoutA]);

        var newFileInGameFolderA = new GamePath(LocationId.Game, "bin/newFileInGameFolderA.txt");
        var newFileFullPathA = GameInstallation.Locations.ToAbsolutePath(newFileInGameFolderA);
        newFileFullPathA.Parent.CreateDirectory();
        await newFileFullPathA.WriteAllTextAsync("New File for this loadout");

        await Synchronizer.ReindexState(GameInstallation);
        LogDiskState(sb, "## 4 - New File Added to Game Folder",
            """
            New files have been added to the game folder by the user or the game, but the loadout hasn't been synchronized yet.
            """, [loadoutA]);

        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 5 - Loadout Synced with New File",
            """
            After the loadout has been synchronized, the new file should be added to the loadout.
            """, [loadoutA]);
        
        var loadoutB = await CreateLoadout();
        var loadoutBState = await SynchronizerService.StatusForLoadout(loadoutB);
        var subBDisposable = loadoutBState.SubscribeSafe(new AnonymousObserver<LoadoutSynchronizerState>(_ => { }, onError: exception => sb.AppendLine($"Loadout B State Error: {exception}")));

        LogDiskState(sb, "## 6 - New Loadout (B) Created - No Sync",
            """
            A new loadout is created, but it has not been synchronized yet. So again the 'Last Synced State' is not set.
            """, [loadoutA, loadoutB]
        );
        
        loadoutB = await Synchronizer.Synchronize(loadoutB);
        
        LogDiskState(sb, "## 7 - New Loadout (B) Synced",
            """
            After the new loadout has been synchronized, the 'Last Synced State' should match the 'Current State' as the loadout has been applied to the game folder. Note that the contents of this 
            loadout are different from the previous loadout due to the new file only being in the previous loadout.
            """, [loadoutA, loadoutB]
        );
        
        var newFileInGameFolderB = new GamePath(LocationId.Game, "bin/newFileInGameFolderB.txt");
        var newFileFullPathB = GameInstallation.Locations.ToAbsolutePath(newFileInGameFolderB);
        newFileFullPathB.Parent.CreateDirectory();
        await newFileFullPathB.WriteAllTextAsync("New File for this loadout, B");
        
        loadoutB = await Synchronizer.Synchronize(loadoutB);
        
        LogDiskState(sb, "## 8 - New File Added to Game Folder (B)",
            """
            A new file has been added to the game folder and B loadout has been synchronized. The new file should be added to the B loadout.
            """, [loadoutA, loadoutB]
        );
        
        await LoadoutManager.DeactivateCurrentLoadout(loadoutA.InstallationInstance);
        await LoadoutManager.ActivateLoadout(loadoutA);

        LogDiskState(sb, "## 9 - Switch back to Loadout A",
            """
            Now we switch back to the A loadout, and the new file should be removed from the game folder.
            """, [loadoutA, loadoutB]
        );
        
        var loadoutC = await LoadoutManager.CopyLoadout(loadoutA);

        LogDiskState(sb, "## 10 - Loadout A Copied to Loadout C",
            """
            Loadout A has been copied to Loadout C, and the contents should match.
            """, [loadoutA, loadoutB, loadoutC]
        );
        
        // Cleanup state subscriptions
        subADisposable.Dispose();
        subBDisposable.Dispose();

        await LoadoutManager.UnManage(GameInstallation);
        
        LogDiskState(sb, "## 11 - Game Unmanaged",
            """
            The loadouts have been deleted and the game folder should be back to its initial state.
            """,
        [loadoutA.Rebase(), loadoutB.Rebase()]);
        
        // We unmanaged the game, so no access to DiskState
        // Check the actual files on disk instead
        await LogFolderState(sb,
            "## 11 - Game Unmanaged",
            originalFileFullPath.Parent.Parent,
            """
            The loadouts have been deleted and the game folder should be back to its initial state.
            """
        );
        
        await Verify(sb.ToString(), extension: "md");
    }

    /// <summary>
    /// Regression test for the roadmap Tier 1 (#2) fix: CopyLoadout must also copy the load order
    /// (SortOrder/SortOrderItem) and the file-conflict priorities (LoadoutItemGroupPriority), which
    /// are keyed to the loadout rather than to LoadoutItems. Before the fix the clone silently lost
    /// them — losing the hand-tuned load order and resolving conflicts by nondeterministic tie-break.
    /// This exercises the SortOrder path; priorities are copied by the same entity-id-remap mechanism.
    /// </summary>
    [Fact]
    public async Task CopyLoadout_CopiesLoadOrder()
    {
        await LoadoutManager.ManageInstallation(GameInstallation);
        var loadoutA = await CreateLoadout();
        LoadoutId loadoutAId = loadoutA;

        // Attach a load order to loadoutA (mirrors how a SortOrderVariety creates one).
        using (var tx = Connection.BeginTransaction())
        {
            _ = new SortOrder.New(tx)
            {
                LoadoutId = loadoutAId,
                ParentEntity = OneOf<LoadoutId, CollectionGroupId>.FromT0(loadoutAId),
                SortOrderTypeId = Guid.NewGuid(),
            };
            await tx.Commit();
        }

        SortOrder.FindByLoadout(Connection.Db, loadoutA).ToArray()
            .Should().ContainSingle("precondition: loadoutA has a load order");

        var loadoutC = await LoadoutManager.CopyLoadout(loadoutA);

        var clonedSortOrders = SortOrder.FindByLoadout(Connection.Db, loadoutC).ToArray();
        clonedSortOrders.Should().ContainSingle("CopyLoadout must copy the SortOrder onto the clone (previously it was silently dropped)");
        clonedSortOrders[0].Loadout.Id.Should().Be(loadoutC.Id, "the copied SortOrder must reference the clone, not the original");
    }

    [Fact]
    [Trait("RequiresNetworking", "False")]
    public async Task PriorityTxFuncs_ResolveAndWinAll_KeepPrioritiesDenseAndOrdered()
    {
        // CODE_REVIEW.md §7 #12: the file-conflict priority TxFuncs feed winning-file resolution
        // and were untested. Exercises ResolveFileConflicts + WinAllFileConflicts end-to-end and
        // asserts the dense 1..N invariant the SQL conflict resolution depends on.
        await LoadoutManager.ManageInstallation(GameInstallation);
        var loadout = await CreateLoadout();
        LoadoutId loadoutId = loadout;

        // Four groups with priorities 1..4 (A, B, C, D).
        var groupIds = new EntityId[4];
        var priorityIds = new LoadoutItemGroupPriorityId[4];
        using (var tx = Connection.BeginTransaction())
        {
            var tempPriorityIds = new EntityId[4];
            for (var i = 0; i < 4; i++)
            {
                var group = new LoadoutItemGroup.New(tx, out var gid)
                {
                    IsGroup = true,
                    LoadoutItem = new LoadoutItem.New(tx, gid)
                    {
                        Name = $"group-{(char)('A' + i)}",
                        LoadoutId = loadoutId,
                    },
                };
                var priority = new LoadoutItemGroupPriority.New(tx)
                {
                    LoadoutId = loadoutId,
                    TargetId = group.LoadoutItemGroupId,
                    Priority = ConflictPriority.From((ulong)(i + 1)),
                };
                groupIds[i] = gid;
                tempPriorityIds[i] = priority.Id;
            }

            var result = await tx.Commit();
            for (var i = 0; i < 4; i++)
            {
                groupIds[i] = result[groupIds[i]];
                priorityIds[i] = LoadoutItemGroupPriorityId.From(result[tempPriorityIds[i]]);
            }
        }

        LoadoutItemGroupPriority.ReadOnly[] Current() => LoadoutItemGroupPriority
            .FindByLoadout(Connection.Db, loadoutId)
            .OrderBy(static p => p.Priority)
            .ToArray();

        // Move D (winner) to just after B (loser): expected order A, B, D, C.
        await LoadoutManager.ResolveFileConflicts(winnerIds: [priorityIds[3]], loserId: priorityIds[1]);

        var after = Current();
        after.Select(p => p.TargetId.Value).Should().Equal(groupIds[0], groupIds[1], groupIds[3], groupIds[2]);
        after.Select(p => p.Priority.Value).Should().Equal(1UL, 2UL, 3UL, 4UL);

        // Priority semantics: HIGHER number wins. WinAllFileConflicts moves A after the current
        // highest (last position): expected order B, D, C, A.
        await LoadoutManager.WinAllFileConflicts(winnerIds: [priorityIds[0]]);

        after = Current();
        after.Select(p => p.TargetId.Value).Should().Equal(groupIds[1], groupIds[3], groupIds[2], groupIds[0]);
        after.Select(p => p.Priority.Value).Should().Equal(1UL, 2UL, 3UL, 4UL);

        // LoseAllFileConflicts moves C to the front (lowest priority, loses everything):
        // expected order C, B, D, A.
        await LoadoutManager.LoseAllFileConflicts(loserIds: [priorityIds[2]]);

        after = Current();
        after.Select(p => p.TargetId.Value).Should().Equal(groupIds[2], groupIds[1], groupIds[3], groupIds[0]);
        after.Select(p => p.Priority.Value).Should().Equal(1UL, 2UL, 3UL, 4UL);
    }

    /// <summary>
    /// Log the state of the actual disk, not the DiskStateEntries
    /// </summary>
    private async Task LogFolderState(StringBuilder sb, string sectionName, AbsolutePath gameFolder, string comments = "")
    {
        Logger.LogInformation("Logging State {SectionName}", sectionName);
        
        sb.AppendLine($"{sectionName}:");
        if (!string.IsNullOrEmpty(comments))
            sb.AppendLine(comments);
        
        var entries = gameFolder.EnumerateFiles(recursive: true)
            .ToArray();
            
        var hashedEntries = new ConcurrentBag<(RelativePath Path, Hash Hash, Size Size)>();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        await Parallel.ForEachAsync(entries, parallelOptions, async (entry, token) =>
        {
            await using var stream = entry.Read();
            var hash = await stream.xxHash3Async(token);
            
            hashedEntries.Add((Path: entry.RelativeTo(gameFolder), Hash: hash, Size: Size.FromLong(stream.Length)));
        });

        sb.AppendLine($"### Current State - ({entries.Length})");
        sb.AppendLine("| Path | Hash | Size |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var (path, hash, size) in hashedEntries.OrderBy(x => x.Path))
        {
            sb.AppendLine($"| `{path}` | {hash} | {size} |");
        }
    }

    [Fact]
    public async Task SwappingLoadoutsDoesNotLeakFiles()
    {
        var sb = new StringBuilder();
        var loadoutA = await CreateLoadout();
        var loadoutB = await CreateLoadout();
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 1 - Loadout A Synced",
            """
            Loadout A has been synchronized, and the game folder should match the loadout.
            """, [loadoutA, loadoutB]);
        
        var newFileInGameFolderA = new GamePath(LocationId.Game, "bin/newFileInGameFolderA.txt");
        var newFileFullPathA = GameInstallation.Locations.ToAbsolutePath(newFileInGameFolderA);
        newFileFullPathA.Parent.CreateDirectory();
        await newFileFullPathA.WriteAllTextAsync("New File for this loadout");

        loadoutA = await Synchronizer.Synchronize(loadoutA);
        await Synchronizer.ReindexState(GameInstallation);

        LogDiskState(sb, "## 2 - New File Added to Game Folder",
            """
            A new file has been added to the game folder, and the loadout has been synchronized. The new file should be added to the loadout.
            """, [loadoutA, loadoutB]);
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 3 - Loadout A Synced with New File",
            """
            Loadout A has been synchronized again, and the new file should be added to the disk state.
            """, [loadoutA, loadoutB]);
        
        loadoutB = await Synchronizer.Synchronize(loadoutB);
        
        LogDiskState(sb, "## 4 - Loadout B Synced",
            """
            Loadout B has been synchronized, the added file should be removed from the disk state, and only exist in loadout A.
            """, [loadoutA, loadoutB]);
        
        
        var tree = await Synchronizer.BuildSyncTree(loadoutA);
        Synchronizer.ProcessSyncTree(tree);
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 5 - Loadout A Synced Again",
            """
            Loadout A has been synchronized again, and the new file should be added to the disk state.
            """, [loadoutA, loadoutB]);
        
        await Verify(sb.ToString(), extension: "md");
    }

    /// <summary>
    /// Regression test for diff-based loadout switching: a file identical in both loadouts (same
    /// path, same content) must not be deleted and re-extracted when switching between them. The
    /// old implementation reset to vanilla before applying the new loadout, so every file the new
    /// loadout wanted looked brand new regardless of whether it already matched what was on disk.
    /// Verified via the file's actual OS-level write time, which only changes on a real rewrite.
    /// </summary>
    [Fact]
    public async Task SwitchingLoadoutsDoesNotRewriteSharedFiles()
    {
        var loadoutA = await CreateLoadout();
        var loadoutB = await CreateLoadout();

        var sharedPath = new GamePath(LocationId.Game, "bin/sharedMod.txt");
        const string sharedContent = "Shared content, identical in both loadouts";

        using (var tx = Connection.BeginTransaction())
        {
            var groupA = AddEmptyGroup(tx, loadoutA, "SharedMod");
            AddFile(tx, loadoutA, groupA, sharedPath, sharedContent, out var hash, out var size);
            await FileStore.BackupFiles([
                new ArchivedFileEntry(new MemoryStreamFactory(sharedPath.Path, new MemoryStream(Encoding.UTF8.GetBytes(sharedContent))), hash, size),
            ]);
            await tx.Commit();
        }

        using (var tx = Connection.BeginTransaction())
        {
            var groupB = AddEmptyGroup(tx, loadoutB, "SharedMod");
            // Same content -> same deterministic hash as loadout A's file; the file store already
            // has it backed up from above, so no second BackupFiles call is needed.
            AddFile(tx, loadoutB, groupB, sharedPath, sharedContent, out _, out _);
            await tx.Commit();
        }

        loadoutA = await Synchronizer.Synchronize(loadoutA);

        var resolvedPath = GameInstallation.Locations.ToAbsolutePath(sharedPath);
        resolvedPath.FileExists.Should().BeTrue();
        var writeTimeBeforeSwitch = resolvedPath.FileInfo.LastWriteTimeUtc;

        loadoutB = await Synchronizer.Synchronize(loadoutB);

        resolvedPath.FileExists.Should().BeTrue("the file is shared between both loadouts and should remain after switching");
        resolvedPath.FileInfo.LastWriteTimeUtc.Should().Be(writeTimeBeforeSwitch,
            "a file identical across both loadouts should not be deleted and re-extracted on switch");
    }

    /// <summary>
    /// Companion to <see cref="SwitchingLoadoutsDoesNotRewriteSharedFiles"/>: a file present at the
    /// same path in both loadouts but with different content must still be correctly overwritten
    /// on switch, proving the diff-based path doesn't skip real changes along with the identical ones.
    /// </summary>
    [Fact]
    public async Task SwitchingLoadoutsOverwritesFilesThatDiffer()
    {
        var loadoutA = await CreateLoadout();
        var loadoutB = await CreateLoadout();

        var path = new GamePath(LocationId.Game, "bin/differingMod.txt");

        using (var tx = Connection.BeginTransaction())
        {
            var groupA = AddEmptyGroup(tx, loadoutA, "ModA");
            AddFile(tx, loadoutA, groupA, path, "Content from A", out var hashA, out var sizeA);
            await FileStore.BackupFiles([
                new ArchivedFileEntry(new MemoryStreamFactory(path.Path, new MemoryStream(Encoding.UTF8.GetBytes("Content from A"))), hashA, sizeA),
            ]);
            await tx.Commit();
        }

        using (var tx = Connection.BeginTransaction())
        {
            var groupB = AddEmptyGroup(tx, loadoutB, "ModB");
            AddFile(tx, loadoutB, groupB, path, "Content from B", out var hashB, out var sizeB);
            await FileStore.BackupFiles([
                new ArchivedFileEntry(new MemoryStreamFactory(path.Path, new MemoryStream(Encoding.UTF8.GetBytes("Content from B"))), hashB, sizeB),
            ]);
            await tx.Commit();
        }

        loadoutA = await Synchronizer.Synchronize(loadoutA);
        var resolvedPath = GameInstallation.Locations.ToAbsolutePath(path);
        (await resolvedPath.ReadAllTextAsync()).Should().Be("Content from A");

        loadoutB = await Synchronizer.Synchronize(loadoutB);
        (await resolvedPath.ReadAllTextAsync()).Should().Be("Content from B");
    }

    [Fact]
    public async Task DeletedFilesStayDeletedWhenModIsReenabled()
    {
        var sb = new StringBuilder();
        var loadoutA = await CreateLoadout();
        var loadoutB = await CreateLoadout();
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 1 - Loadout A Synced",
            """
            Loadout A has been synchronized, and the game folder should match the loadout.
            """, [loadoutA, loadoutB]);
        
        var modFile = FileSystem.GetKnownPath(KnownPath.EntryDirectory) / "Resources" / "TestMod.zip";
        var libraryFile = await LibraryService.AddLocalFile(modFile);
        var mod = (await LoadoutManager.InstallItem(libraryFile.AsLibraryFile().AsLibraryItem(), loadoutA)).LoadoutItemGroup!.Value;
        loadoutA = loadoutA.Rebase();
        
        LogDiskState(sb, "## 2 - Loadout A Mod Added",
            """
            A mod has been added but not yet synced, so only the loadout has the file.
            """, [loadoutA, loadoutB]);
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 3 - Loadout A Synced",
            """
            Loadout A has been synchronized, and the game folder should match the loadout.
            """, [loadoutA, loadoutB]);
        
        var testFilePath = new GamePath(LocationId.Game, "bin/x64/ThisIsATestFile.txt");
        var otherTestFilePath = new GamePath(LocationId.Game, "bin/x64/And Another One.txt");
        
        var diskPath = loadoutA.InstallationInstance.Locations.ToAbsolutePath(testFilePath);
        var otherDiskPath = loadoutA.InstallationInstance.Locations.ToAbsolutePath(otherTestFilePath);
        diskPath.Delete();
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 4 - Deleted file from disk",
            """
            A mod file has been deleted from disk, so that information should be synced to the loadout.
            """, [loadoutA, loadoutB]);
        

        using var tx = Connection.BeginTransaction();
        tx.Add(mod, LoadoutItem.Disabled, Null.Instance);
        await tx.Commit();
        
        loadoutA = loadoutA.Rebase();

        LogDiskState(sb, "## 5 - Disabled the mod group",
            """
            The mod has been disabled, but not yet synched
            """, [loadoutA, loadoutB]);
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 6 - Loadout A Synced",
            """
            Loadout A has been synchronized, the mod files shouldn't show back up.
            """, [loadoutA, loadoutB]);
        
        // Re-enable the mod
        using var tx2 = Connection.BeginTransaction();
        tx2.Retract(mod, LoadoutItem.Disabled, Null.Instance);
        await tx2.Commit();
        
        loadoutA = loadoutA.Rebase();
        
        LogDiskState(sb, "## 6 - Enable the mod group",
            """
            Re-enable the mod.
            """, [loadoutA, loadoutB]);
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 7 - Loadout A Synced",
            """
            Re-enable the mod.
            """, [loadoutA, loadoutB]);
        
        diskPath.FileExists.Should().BeFalse("The file should still be deleted");
        
        loadoutB = await Synchronizer.Synchronize(loadoutB);
        
        LogDiskState(sb, "## 8 - Loadout B Synced",
            """
            Loadout B has been synchronized, the file should still be deleted as well as the other mod file.
            """, [loadoutA, loadoutB]);
        
        diskPath.FileExists.Should().BeFalse("The file should still be deleted");
        otherDiskPath.FileExists.Should().BeFalse("The other file is in a mod not in this loadout");
        
        loadoutA = await Synchronizer.Synchronize(loadoutA);
        
        LogDiskState(sb, "## 9 - Loadout A Synced",
            """
            Loadout A has been synchronized, the file should still be deleted but the other file in the mod should be back.
            """, [loadoutA, loadoutB]);
        
        diskPath.FileExists.Should().BeFalse("The file should still be deleted");
        otherDiskPath.FileExists.Should().BeTrue("This file is not deleted and is in a mod in this loadout");
        
        await Verify(sb.ToString(), extension: "md");
    }
}
