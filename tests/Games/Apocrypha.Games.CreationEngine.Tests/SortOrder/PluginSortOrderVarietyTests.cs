using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.CreationEngine.SortOrder;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using OneOf;

namespace Apocrypha.Games.CreationEngine.Tests.SortOrder;

/// <summary>
/// Datastore-backed coverage for the plugin sort order: seeding a curated order, reconciling
/// later-installed plugins, and reading it back. No game install, no network.
/// </summary>
/// <remarks>
/// Uses the assembly-level DI (SkyrimSE universal locator from <see cref="Startup"/>) rather than
/// <c>AIsolatedGameTest</c>: the isolated variant starts the app's hosted services, whose Linux
/// protocol-handler registration shells out to <c>update-desktop-database</c> — absent on CI
/// runners, which kills the host before the game registry populates. The variety under test is
/// game-agnostic, so which Creation Engine game hosts it does not matter.
/// </remarks>
public class PluginSortOrderVarietyTests(IServiceProvider serviceProvider) : AGameTest<CreationEngine.SkyrimSE.SkyrimSE>(serviceProvider)
{
    [Fact]
    public async Task CuratedOrderPersistsAndReconciles()
    {
        var loadout = await CreateLoadout();
        var variety = ServiceProvider.GetRequiredService<PluginSortOrderVariety>();

        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, new RelativePath[] { "Data/Alpha.esp", "Data/Bravo.esp", "Data/Charlie.esm" }, loadout, "First Mod");
            await tx.Commit();
        }

        // Seed a curated order. It places an ESP block ahead of where (class, name) would, and
        // matches the loadout case-insensitively; the ESM it does not mention appends after.
        await variety.ApplyCuratedOrder(
            loadout.LoadoutId,
            OneOf<LoadoutId, CollectionGroupId>.FromT0(loadout.LoadoutId),
            ["Bravo.esp", "ALPHA.esp", "NotInstalled.esp"]);

        variety.GetPersistedPluginOrder(loadout.LoadoutId)
            .Should().Equal("Bravo.esp", "ALPHA.esp", "Charlie.esm");

        // Plugins installed later reconcile to the end in (class, name) order — greater index
        // wins for Creation Engine — without disturbing the curated block.
        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, new RelativePath[] { "Data/Delta.esp", "Data/Anna.esl" }, loadout, "Second Mod");
            await tx.Commit();
        }

        var sortOrderId = variety.GetSortOrderIdFor(loadout.LoadoutId);
        sortOrderId.HasValue.Should().BeTrue();
        await variety.ReconcileSortOrder(sortOrderId.Value);

        variety.GetPersistedPluginOrder(loadout.LoadoutId)
            .Should().Equal("Bravo.esp", "ALPHA.esp", "Charlie.esm", "Anna.esl", "Delta.esp");

        // The reactive items surface loadout truth: on-disk casing for display, enabled state.
        var items = variety.GetSortOrderItems(sortOrderId.Value, Connection.Db);
        items.Select(item => item.DisplayName).Should().Equal("Bravo.esp", "Alpha.esp", "Charlie.esm", "Anna.esl", "Delta.esp");
        items.Should().OnlyContain(item => item.IsActive);
    }
}
