using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Collections;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.Loadouts.Synchronizers.Conflicts;
using Apocrypha.Games.TestFramework;
using Apocrypha.Sdk;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.Loadouts;
using Xunit.Abstractions;
using Loadout = Apocrypha.Sdk.Loadouts.Loadout;

namespace Apocrypha.DataModel.Tests;

public class LoadoutManagerMoveItemsTests : ACyberpunkIsolatedGameTest<LoadoutManagerMoveItemsTests>
{
    private readonly ILibraryService _libraryService;
    private readonly IConnection _connection;

    public LoadoutManagerMoveItemsTests(ITestOutputHelper helper) : base(helper)
    {
        _libraryService = ServiceProvider.GetRequiredService<ILibraryService>();
        _connection = ServiceProvider.GetRequiredService<IConnection>();
    }

    [Fact]
    public async Task MoveItems_ReparentsItem_AndKeepsPriority()
    {
        var loadout = await CreateTestLoadout("Test Loadout");
        var source = await CreateCollectionInLoadout(loadout, "My Mods");
        var target = await CreateCollectionInLoadout(loadout, "Second Collection");

        var group = await InstallTestItem(loadout, source);
        var priorityCountBefore = LoadoutItemGroupPriority.FindByLoadout(_connection.Db, loadout.LoadoutId).Count;

        var moved = await LoadoutManager.MoveItems([group.LoadoutItemGroupId], target.CollectionGroupId);

        moved.Should().Be(1);

        var reloaded = LoadoutItemGroup.Load(_connection.Db, group.LoadoutItemGroupId);
        reloaded.AsLoadoutItem().ParentId.Should().Be(target.AsLoadoutItemGroup().LoadoutItemGroupId);

        // Priorities are loadout-wide; a move must not create or drop any
        var priorityCountAfter = LoadoutItemGroupPriority.FindByLoadout(_connection.Db, loadout.LoadoutId).Count;
        priorityCountAfter.Should().Be(priorityCountBefore);
    }

    [Fact]
    public async Task MoveItems_ToSameCollection_IsNoOp()
    {
        var loadout = await CreateTestLoadout("Test Loadout");
        var source = await CreateCollectionInLoadout(loadout, "My Mods");

        var group = await InstallTestItem(loadout, source);

        var moved = await LoadoutManager.MoveItems([group.LoadoutItemGroupId], source.CollectionGroupId);

        moved.Should().Be(0);
        LoadoutItemGroup.Load(_connection.Db, group.LoadoutItemGroupId)
            .AsLoadoutItem().ParentId.Should().Be(source.AsLoadoutItemGroup().LoadoutItemGroupId);
    }

    [Fact]
    public async Task MoveItems_ToReadOnlyCollection_Throws()
    {
        var loadout = await CreateTestLoadout("Test Loadout");
        var source = await CreateCollectionInLoadout(loadout, "My Mods");
        var readOnly = await CreateCollectionInLoadout(loadout, "ReadOnly Collection", isReadOnly: true);

        var group = await InstallTestItem(loadout, source);

        var act = async () => await LoadoutManager.MoveItems([group.LoadoutItemGroupId], readOnly.CollectionGroupId);
        await act.Should().ThrowAsync<InvalidOperationException>(because: "read-only collections don't accept new mods");
    }

    [Fact]
    public async Task MoveItems_SkipsCollectionManagedItems()
    {
        var loadout = await CreateTestLoadout("Test Loadout");
        var source = await CreateCollectionInLoadout(loadout, "Source Collection");
        var target = await CreateCollectionInLoadout(loadout, "Target Collection");

        var group = await InstallTestItem(loadout, source);

        // Mark the group as delivered by a collection (optional item: IsRequired = false —
        // presence of the attribute is what makes it collection-managed)
        using (var tx = _connection.BeginTransaction())
        {
            tx.Add(group.Id, NexusCollectionItemLoadoutGroup.IsRequired, false);
            await tx.Commit();
        }

        var moved = await LoadoutManager.MoveItems([group.LoadoutItemGroupId], target.CollectionGroupId);

        moved.Should().Be(0, because: "items delivered by a collection stay with it");
        LoadoutItemGroup.Load(_connection.Db, group.LoadoutItemGroupId)
            .AsLoadoutItem().ParentId.Should().Be(source.AsLoadoutItemGroup().LoadoutItemGroupId);
    }

    [Fact]
    public async Task MoveItems_SkipsItemsFromOtherLoadouts()
    {
        var loadout1 = await CreateTestLoadout("Loadout1");
        var loadout2 = await CreateTestLoadout("Loadout2");
        var source = await CreateCollectionInLoadout(loadout1, "My Mods");
        var otherLoadoutTarget = await CreateCollectionInLoadout(loadout2, "Other Loadout Collection");

        var group = await InstallTestItem(loadout1, source);

        var moved = await LoadoutManager.MoveItems([group.LoadoutItemGroupId], otherLoadoutTarget.CollectionGroupId);

        moved.Should().Be(0, because: "items can only move within their own loadout");
        LoadoutItemGroup.Load(_connection.Db, group.LoadoutItemGroupId)
            .AsLoadoutItem().ParentId.Should().Be(source.AsLoadoutItemGroup().LoadoutItemGroupId);
    }

    private async Task<LoadoutItemGroup.ReadOnly> InstallTestItem(Loadout.ReadOnly loadout, CollectionGroup.ReadOnly parent)
    {
        var archivePath = FileSystem.GetKnownPath(KnownPath.CurrentDirectory)
            .Combine("Resources")
            .Combine("nested_archive.zip");
        var libraryItem = (await _libraryService.AddLocalFile(archivePath)).AsLibraryFile().AsLibraryItem();

        var result = await LoadoutManager.InstallItem(libraryItem, loadout.LoadoutId, parent: parent.AsLoadoutItemGroup().LoadoutItemGroupId);
        return result.LoadoutItemGroup!.Value;
    }

    private async Task<Loadout.ReadOnly> CreateTestLoadout(string name)
    {
        using var tx = _connection.BeginTransaction();

        var metadata = new GameInstallMetadata.New(tx)
        {
            Path = GameInstallation.LocatorResult.Path.ToString(),
            Name = GameInstallation.Game.DisplayName,
            Store = GameInstallation.LocatorResult.Store,
            GameId = GameInstallation.Game.NexusModsGameId.Value,
        };

        var loadoutNew = new Loadout.New(tx)
        {
            Name = name,
            ShortName = name,
            InstallationId = metadata,
            LoadoutKind = LoadoutKind.Default,
            Revision = 0,
            GameVersion = VanityVersion.From("Unknown"),
        };

        var result = await tx.Commit();
        return result.Remap(loadoutNew);
    }

    private async Task<CollectionGroup.ReadOnly> CreateCollectionInLoadout(
        Loadout.ReadOnly loadout,
        string name,
        bool isReadOnly = false)
    {
        using var tx = _connection.BeginTransaction();

        var group = new CollectionGroup.New(tx, out var collectionId)
        {
            IsReadOnly = isReadOnly,
            LoadoutItemGroup = new LoadoutItemGroup.New(tx, collectionId)
            {
                IsGroup = true,
                LoadoutItem = new LoadoutItem.New(tx, collectionId)
                {
                    Name = name,
                    LoadoutId = loadout.LoadoutId,
                },
            },
        };

        var result = await tx.Commit();
        return result.Remap(group);
    }
}
