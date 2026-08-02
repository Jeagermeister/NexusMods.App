using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.RedEngine.Cyberpunk2077;
using Apocrypha.Games.RedEngine.Cyberpunk2077.Extensions;
using Apocrypha.Games.RedEngine.Cyberpunk2077.Models;
using Apocrypha.Games.RedEngine.Cyberpunk2077.SortOrder;
using Apocrypha.Games.TestFramework;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using OneOf;
using Xunit.Abstractions;

namespace Apocrypha.Games.RedEngine.Tests;

/// <summary>
/// The full case-fold for REDmod sort items (deferred-work ledger item 11, S4-1 residual):
/// keys fold to lower case everywhere while display casing survives through persistence and
/// modlist.txt. Mirrors the Creation Engine's <c>PluginSortItemData</c> pattern.
/// </summary>
public class RedModCaseFoldTests : ACyberpunkIsolatedGameTest<Cyberpunk2077Game>
{
    private readonly RedModDeployTool _tool;

    public RedModCaseFoldTests(ITestOutputHelper helper) : base(helper)
    {
        _tool = ServiceProvider.GetServices<ITool>().OfType<RedModDeployTool>().Single();
    }

    [Fact]
    public void KeysFoldWhileDisplayCasingSurvives()
    {
        var folder = RelativePath.FromUnsanitizedInput("Driver_Shotguns");

        var reactive = new RedModReactiveSortItem(0, folder, "mod", isActive: true);
        reactive.Key.Key.Should().Be("driver_shotguns");
        reactive.RedModFolderName.ToString().Should().Be("Driver_Shotguns");
        reactive.DisplayName.Should().Be("Driver_Shotguns");

        var sortData = new RedModSortItemData(folder, 3);
        sortData.Key.Key.Should().Be("driver_shotguns");
        sortData.RedModFolderName.ToString().Should().Be("Driver_Shotguns");

        var loadoutData = new RedModSortItemLoadoutData(folder, isEnabled: true, "mod", DynamicData.Kernel.Optional<LoadoutItemGroupId>.None);
        loadoutData.Key.Key.Should().Be("driver_shotguns");
        loadoutData.RedModFolderName.ToString().Should().Be("Driver_Shotguns");

        // Case variants of the same folder produce the same key — the whole point.
        // (Compared via Equals: SortItemKey implements IEquatable but not object.Equals,
        // so FluentAssertions' Be() would fall back to reference equality.)
        new RedModSortItemData(RelativePath.FromUnsanitizedInput("DRIVER_SHOTGUNS"), 0).Key.Equals(sortData.Key)
            .Should().BeTrue();
    }

    /// <summary>
    /// The S4-1 residual scenario end to end: the persisted row carries one casing, the deployed
    /// loadout another (a reinstalled archive can legitimately re-case its folder). Before the
    /// fold the reconcile missed the row — the mod was treated as new and its position reset —
    /// and a UI move addressed a key that no longer matched anything.
    /// </summary>
    [Fact]
    public async Task ReCasedPersistedRowKeepsPositionCasingAndStaysMovable()
    {
        var loadout = await CreateLoadout();
        var sortOrderManager = InitAndGetSortOrderManager();
        var variety = ServiceProvider.GetRequiredService<RedModSortOrderVariety>();

        loadout = await AddRedMods(loadout);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await sortOrderManager.UpdateLoadOrders(loadout.LoadoutId, token: cts.Token);
        loadout = loadout.Rebase();

        var sortOrderId = variety.GetSortOrderIdFor(OneOf<LoadoutId, CollectionGroupId>.FromT0(loadout.LoadoutId), Connection.Db);
        sortOrderId.HasValue.Should().BeTrue();

        var before = variety.GetSortOrderItems(sortOrderId.Value, Connection.Db);
        var target = before.Single(item => item.DisplayName == "Driver_Shotguns");

        // Re-case the persisted row, simulating a row written from an earlier install whose
        // archive shipped different casing; the loadout keeps deploying "Driver_Shotguns".
        // Delete + recreate rather than update: RelativePath equality is what the attribute
        // dedupes on, so a case-variant update can be dropped as an identical value.
        var dbRow = Connection.Db.RetrieveRedModSortableEntries(sortOrderId.Value)
            .Single(row => RedModReactiveSortItem.MakeKey(row.RedModFolderName).Equals(target.Key));
        var originalIndex = dbRow.AsSortOrderItem().SortIndex;
        using (var tx = Connection.BeginTransaction())
        {
            tx.Delete(dbRow.Id, recursive: false);
            var replacement = new SortOrderItem.New(tx)
            {
                ParentSortOrderId = sortOrderId.Value,
                SortIndex = originalIndex,
            };
            _ = new RedModSortOrderItem.New(tx, replacement)
            {
                SortOrderItem = replacement,
                RedModFolderName = RelativePath.FromUnsanitizedInput("DRIVER_SHOTGUNS"),
            };
            await tx.Commit();
        }

        // Premise guard: the simulation only means something if the stored casing really differs.
        Connection.Db.RetrieveRedModSortableEntries(sortOrderId.Value)
            .Single(row => RedModReactiveSortItem.MakeKey(row.RedModFolderName).Equals(target.Key))
            .RedModFolderName.ToString()
            .Should().Be("DRIVER_SHOTGUNS", "the persisted row must actually carry the variant casing for this test to test anything");

        // Position must be kept: a case-variant miss used to drop the row from the reconcile and
        // reinsert the mod as new at the top.
        var after = variety.GetSortOrderItems(sortOrderId.Value, Connection.Db);
        var reCased = after.Single(item => item.Key.Equals(target.Key));
        reCased.SortIndex.Should().Be(target.SortIndex, "a re-cased persisted row is the same mod, not a new one");
        reCased.DisplayName.Should().Be("Driver_Shotguns", "display casing comes from the deployed loadout, not the stale persisted row");

        // modlist.txt is a display-casing contract: the loadout casing, never the persisted
        // variant, never the folded key.
        loadout = loadout.Rebase();
        await using var tempFile = TemporaryFileManager.CreateFile();
        await _tool.WriteLoadOrderFile(tempFile.Path, loadout);
        var modlist = await tempFile.Path.ReadAllTextAsync();
        modlist.Should().Contain("Driver_Shotguns");
        modlist.Should().NotContain("DRIVER_SHOTGUNS");
        modlist.Should().NotContain("driver_shotguns");

        // The UI addresses items by folded key, so a move against the re-cased row must still
        // land instead of warning and doing nothing. Driver_Shotguns sits at the end of the
        // fixture order, so move it up — a +1 there would be clamped into a no-op.
        target.SortIndex.Should().BeGreaterThan(0, "the fixture must leave room to move the item up");
        using var moveCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await variety.MoveItemDelta(sortOrderId.Value, reCased.Key, delta: -1, token: moveCts.Token);

        var moved = variety.GetSortOrderItems(sortOrderId.Value, Connection.Db)
            .Single(item => item.Key.Equals(target.Key));
        moved.SortIndex.Should().Be(target.SortIndex - 1, "the move must resolve the folded key against the re-cased persisted row");
    }

    private async Task<Loadout.ReadOnly> AddRedMods(Loadout.ReadOnly loadout)
    {
        var files = new[] { "one_mod.7z", "several_red_mods.7z" };

        await using var tempDir = TemporaryFileManager.CreateFolder();
        foreach (var file in files)
        {
            var sourcePath = FileSystem.GetKnownPath(KnownPath.EntryDirectory).Combine("LibraryArchiveInstallerTests/Resources/" + file);
            var copyPath = tempDir.Path.Combine(file);
            // Create copy to avoid "file in use" by other tests issues
            File.Copy(sourcePath.ToString(), copyPath.ToString(), overwrite: true);

            var libraryArchive = await RegisterLocalArchive(copyPath);
            _ = await LoadoutManager.InstallItem(libraryArchive.AsLibraryFile().AsLibraryItem(), loadout);
        }

        return loadout.Rebase();
    }
}
