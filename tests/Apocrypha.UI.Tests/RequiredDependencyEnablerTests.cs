using FluentAssertions;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.ElementComparers;
using NexusMods.Paths;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.App.UI.Pages.LoadoutPage;
using Apocrypha.Sdk.Library;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.Sdk.NexusModsApi;

namespace Apocrypha.UI.Tests;

/// <summary>
/// Directed tests for <see cref="RequiredDependencyEnabler"/>: enabling a mod should also turn on
/// any Nexus-required mod that is already installed in the same loadout but currently disabled,
/// transitively, without touching absent mods or unrelated ones.
/// </summary>
public class RequiredDependencyEnablerTests : AUiTest
{
    private const uint GameId = 1303; // arbitrary Nexus game id, shared across the loadout
    private readonly IConnection _connection;

    public RequiredDependencyEnablerTests(IServiceProvider provider) : base(provider)
    {
        _connection = Connection;
    }

    /// <summary>
    /// Creates a Nexus-sourced mod installed into <paramref name="loadoutId"/> and returns the
    /// loadout item (group) id. The mod page is keyed by <paramref name="modId"/> so requirements
    /// can reference it.
    /// </summary>
    private static EntityId AddNexusMod(ITransaction tx, LoadoutId loadoutId, uint modId, bool disabled)
    {
        var modPageId = tx.TempId();
        _ = new NexusModsModPageMetadata.New(tx, modPageId)
        {
            Uid = new ModUid(ModId.From(modId), NexusModsGameId.From(GameId)),
            Name = $"Mod {modId}",
            GameDomain = GameDomain.From("testgame"),
            UpdatedAt = DateTimeOffset.UtcNow,
            DataUpdatedAt = DateTimeOffset.UtcNow,
        };

        var fileMetadataId = tx.TempId();
        _ = new NexusModsFileMetadata.New(tx, fileMetadataId)
        {
            Uid = new FileUid(FileId.From(modId), NexusModsGameId.From(GameId)),
            Name = $"Mod {modId} file",
            Version = "1.0.0",
            UploadedAt = DateTimeOffset.UtcNow,
            ModPageId = NexusModsModPageMetadataId.From(modPageId),
        };

        var libraryItemId = tx.TempId();
        _ = new NexusModsLibraryItem.New(tx, libraryItemId)
        {
            FileMetadataId = NexusModsFileMetadataId.From(fileMetadataId),
            ModPageMetadataId = NexusModsModPageMetadataId.From(modPageId),
            LibraryItem = new LibraryItem.New(tx, libraryItemId)
            {
                Name = $"Mod {modId}",
            },
        };

        var groupId = tx.TempId();
        var group = new LoadoutItemGroup.New(tx, groupId)
        {
            IsGroup = true,
            LoadoutItem = new LoadoutItem.New(tx, groupId)
            {
                Name = $"Mod {modId}",
                LoadoutId = loadoutId,
            },
        };
        _ = new LibraryLinkedLoadoutItem.New(tx, groupId)
        {
            LoadoutItemGroup = group,
            LibraryItemId = LibraryItemId.From(libraryItemId),
        };

        if (disabled) tx.Add(groupId, LoadoutItem.Disabled, Null.Instance);

        return groupId;
    }

    private static void AddRequirement(ITransaction tx, uint ownerModId, uint requiredModId)
    {
        _ = new NexusModsModRequirement.New(tx)
        {
            OwnerUid = new ModUid(ModId.From(ownerModId), NexusModsGameId.From(GameId)),
            RequiredUid = new ModUid(ModId.From(requiredModId), NexusModsGameId.From(GameId)),
            RequiredModName = $"Mod {requiredModId}",
        };
    }

    [Fact]
    public async Task EnablingMod_EnablesInstalledButDisabledRequirement()
    {
        using var tx = _connection.BeginTransaction();
        var loadoutId = LoadoutId.From(tx.TempId());

        var dependant = AddNexusMod(tx, loadoutId, modId: 100, disabled: true);
        var framework = AddNexusMod(tx, loadoutId, modId: 200, disabled: true);
        AddRequirement(tx, ownerModId: 100, requiredModId: 200);
        var result = await tx.Commit();

        var item = LoadoutItem.Load(_connection.Db, result[dependant]);
        var toEnable = RequiredDependencyEnabler.GetDependenciesToEnable(_connection.Db, [item]);

        toEnable.Should().ContainSingle().Which.Should().Be(result[framework]);
    }

    [Fact]
    public async Task AlreadyEnabledRequirement_IsNotReturned()
    {
        using var tx = _connection.BeginTransaction();
        var loadoutId = LoadoutId.From(tx.TempId());

        var dependant = AddNexusMod(tx, loadoutId, modId: 100, disabled: true);
        _ = AddNexusMod(tx, loadoutId, modId: 200, disabled: false); // framework already on
        AddRequirement(tx, ownerModId: 100, requiredModId: 200);
        var result = await tx.Commit();

        var item = LoadoutItem.Load(_connection.Db, result[dependant]);
        RequiredDependencyEnabler.GetDependenciesToEnable(_connection.Db, [item])
            .Should().BeEmpty("an already-enabled requirement needs no action");
    }

    [Fact]
    public async Task MissingRequirement_IsIgnored()
    {
        using var tx = _connection.BeginTransaction();
        var loadoutId = LoadoutId.From(tx.TempId());

        var dependant = AddNexusMod(tx, loadoutId, modId: 100, disabled: true);
        // Required mod 200 is NOT installed in the loadout.
        AddRequirement(tx, ownerModId: 100, requiredModId: 200);
        var result = await tx.Commit();

        var item = LoadoutItem.Load(_connection.Db, result[dependant]);
        RequiredDependencyEnabler.GetDependenciesToEnable(_connection.Db, [item])
            .Should().BeEmpty("a requirement that isn't installed is left to the diagnostics system");
    }

    [Fact]
    public async Task Requirements_AreResolvedTransitively()
    {
        using var tx = _connection.BeginTransaction();
        var loadoutId = LoadoutId.From(tx.TempId());

        // A -> B -> C, with B and C installed-but-disabled.
        var a = AddNexusMod(tx, loadoutId, modId: 100, disabled: true);
        var b = AddNexusMod(tx, loadoutId, modId: 200, disabled: true);
        var c = AddNexusMod(tx, loadoutId, modId: 300, disabled: true);
        AddRequirement(tx, ownerModId: 100, requiredModId: 200);
        AddRequirement(tx, ownerModId: 200, requiredModId: 300);
        var result = await tx.Commit();

        var item = LoadoutItem.Load(_connection.Db, result[a]);
        RequiredDependencyEnabler.GetDependenciesToEnable(_connection.Db, [item])
            .Should().BeEquivalentTo([result[b], result[c]]);
    }

    [Fact]
    public async Task UnrelatedDisabledMod_IsNotEnabled()
    {
        using var tx = _connection.BeginTransaction();
        var loadoutId = LoadoutId.From(tx.TempId());

        var dependant = AddNexusMod(tx, loadoutId, modId: 100, disabled: true);
        var framework = AddNexusMod(tx, loadoutId, modId: 200, disabled: true);
        var unrelated = AddNexusMod(tx, loadoutId, modId: 999, disabled: true);
        AddRequirement(tx, ownerModId: 100, requiredModId: 200);
        var result = await tx.Commit();

        var item = LoadoutItem.Load(_connection.Db, result[dependant]);
        var toEnable = RequiredDependencyEnabler.GetDependenciesToEnable(_connection.Db, [item]);

        toEnable.Should().ContainSingle().Which.Should().Be(result[framework]);
        toEnable.Should().NotContain(result[unrelated]);
    }
}
