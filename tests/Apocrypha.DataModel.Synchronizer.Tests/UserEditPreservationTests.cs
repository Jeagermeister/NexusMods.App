using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk.Games;
using Xunit.Abstractions;

namespace Apocrypha.DataModel.Synchronizer.Tests;

/// <summary>
/// Locks in the promise that a user's hand-edit to a mod-deployed file survives Apply.
/// </summary>
/// <remarks>
/// The mechanism under test is engine-agnostic: an externally modified file signatures as
/// "disk differs, previous and loadout agree" (<c>ABB</c>), which maps to
/// <c>IngestFromDisk</c>; the ingested copy lands in the Overrides group, which sits on
/// layer 2 of the winning-files query and therefore beats every mod file at the same path.
/// The motivating case was a Buffout4 <c>config.toml</c> whose hand-tuned <c>MaxStdIO</c>
/// is the difference between a modlist that boots and one that crashes at the main menu —
/// a revert-on-Apply there costs the user their game, silently.
/// </remarks>
public class UserEditPreservationTests(ITestOutputHelper helper) : ACyberpunkIsolatedGameTest<UserEditPreservationTests>(helper)
{
    private const string UserEdit = "MaxStdIO = 2048  # hand-tuned";

    [Fact]
    public async Task UserEditsToDeployedModFilesSurviveApply()
    {
        var loadout = await CreateLoadout();
        loadout = await Synchronizer.Synchronize(loadout);

        var configPath = new GamePath(LocationId.Game, "bin/mods/tweakMod/config.toml");
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, [configPath.Path], loadout, "TweakMod");
            await tx.Commit();
        }
        Refresh(ref loadout);
        loadout = await Synchronizer.Synchronize(loadout);
        await Synchronizer.ReindexState(GameInstallation);

        var onDisk = GameInstallation.Locations.ToAbsolutePath(configPath);
        onDisk.FileExists.Should().BeTrue("the mod's config must deploy before the user can tune it");

        // The user tunes the deployed file outside the app.
        await onDisk.WriteAllTextAsync(UserEdit);

        // First Apply after the edit: this is the moment a naive synchronizer would revert.
        loadout = await Synchronizer.Synchronize(loadout);
        await Synchronizer.ReindexState(GameInstallation);
        Refresh(ref loadout);

        (await onDisk.ReadAllTextAsync()).Should().Be(UserEdit,
            "Apply must ingest a user's edit, not revert it");

        var itemsAtPath = LoadoutItem.FindByLoadout(Connection.Db, loadout)
            .OfTypeLoadoutItemWithTargetPath()
            .Where(item => (GamePath)item.TargetPath == configPath)
            .ToArray();
        itemsAtPath.Should().HaveCount(2,
            "the mod's original file stays untouched and the edit becomes a separate override");

        // Steady state: further Applies keep the edit.
        loadout = await Synchronizer.Synchronize(loadout);
        (await onDisk.ReadAllTextAsync()).Should().Be(UserEdit,
            "the ingested override outranks the mod's copy on every later Apply");
    }

    [Fact]
    public async Task UserEditsSurviveTheOwningModBeingDisabled()
    {
        // Tonight's real-world shape: a collection repair disables/reinstalls the mod that
        // deployed the config the user had tuned. The override must not die with the mod.
        var loadout = await CreateLoadout();
        loadout = await Synchronizer.Synchronize(loadout);

        var configPath = new GamePath(LocationId.Game, "bin/mods/tweakMod/config.toml");
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, [configPath.Path], loadout, "TweakMod");
            await tx.Commit();
        }
        Refresh(ref loadout);
        loadout = await Synchronizer.Synchronize(loadout);
        await Synchronizer.ReindexState(GameInstallation);

        var onDisk = GameInstallation.Locations.ToAbsolutePath(configPath);
        await onDisk.WriteAllTextAsync(UserEdit);

        // Ingest the edit.
        loadout = await Synchronizer.Synchronize(loadout);
        await Synchronizer.ReindexState(GameInstallation);
        Refresh(ref loadout);

        // Disable the mod that originally deployed the file.
        using (var tx = Connection.BeginTransaction())
        {
            var mod = LoadoutItem.FindByLoadout(Connection.Db, loadout).First(item => item.Name == "TweakMod");
            tx.Add(mod.Id, LoadoutItem.Disabled, NexusMods.MnemonicDB.Abstractions.ElementComparers.Null.Instance);
            await tx.Commit();
        }
        Refresh(ref loadout);
        loadout = await Synchronizer.Synchronize(loadout);
        await Synchronizer.ReindexState(GameInstallation);

        (await onDisk.ReadAllTextAsync()).Should().Be(UserEdit,
            "the override belongs to the user, not to the mod that first deployed the file");
    }
}
