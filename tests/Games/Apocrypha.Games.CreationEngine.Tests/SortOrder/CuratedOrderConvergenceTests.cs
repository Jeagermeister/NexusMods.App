using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.CreationEngine.Models;
using Apocrypha.Games.CreationEngine.SortOrder;
using Apocrypha.Games.TestFramework;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.StandardGameLocators.TestHelpers.StubbedGames;
using OneOf;

namespace Apocrypha.Games.CreationEngine.Tests.SortOrder;

/// <summary>
/// The seeder can race other sort-order writers during a collection install (review finding
/// B-9): these tests lock in the observable halves of that contract -- re-seeding is
/// idempotent, and re-seeding over externally perturbed rows converges back to the curated
/// order without throwing or duplicating entries. (The raw TryPersistSortOrder CAS-retry
/// path is not directly reachable from public API; see the review ledger.)
/// </summary>
/// <remarks>
/// Plugin names are unique to this class ("Conv*"): the datastore is shared across parallel
/// test classes, so global row scans or common names ("Alpha.esp") bleed between tests.
/// A duplicated row would surface as a repeated name in GetPersistedPluginOrder, so the
/// order assertions below also cover row shape.
/// </remarks>
public class CuratedOrderConvergenceTests(IServiceProvider serviceProvider) : AGameTest<StubbedGame>(serviceProvider)
{
    [Fact]
    public async Task ReseedingIsIdempotent()
    {
        var loadout = await CreateLoadout();
        var variety = ServiceProvider.GetRequiredService<PluginSortOrderVariety>();

        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, new RelativePath[] { "Data/ConvIdemA.esp", "Data/ConvIdemB.esp" }, loadout, "Mods");
            await tx.Commit();
        }

        await variety.ApplyCuratedOrder(loadout.LoadoutId, OneOf<LoadoutId, CollectionGroupId>.FromT0(loadout.LoadoutId), ["ConvIdemB.esp", "ConvIdemA.esp"]);
        await variety.ApplyCuratedOrder(loadout.LoadoutId, OneOf<LoadoutId, CollectionGroupId>.FromT0(loadout.LoadoutId), ["ConvIdemB.esp", "ConvIdemA.esp"]);

        // Re-installing the same collection must neither reorder nor duplicate entries.
        variety.GetPersistedPluginOrder(loadout.LoadoutId)
            .Should().Equal("ConvIdemB.esp", "ConvIdemA.esp");
    }

    [Fact]
    public async Task ReseedingAfterExternalPerturbationConverges()
    {
        var loadout = await CreateLoadout();
        var variety = ServiceProvider.GetRequiredService<PluginSortOrderVariety>();

        using (var tx = Connection.BeginTransaction())
        {
            await AddModAsync(tx, new RelativePath[] { "Data/ConvPertA.esp", "Data/ConvPertB.esp" }, loadout, "Mods");
            await tx.Commit();
        }

        await variety.ApplyCuratedOrder(loadout.LoadoutId, OneOf<LoadoutId, CollectionGroupId>.FromT0(loadout.LoadoutId), ["ConvPertB.esp", "ConvPertA.esp"]);

        // Stand-in for the concurrent writer the CAS retry exists for: flip the persisted
        // indices behind the variety's back. Scoped to this test's rows only -- the
        // datastore is shared with parallel test classes.
        using (var tx = Connection.BeginTransaction())
        {
            foreach (var item in PluginSortOrderItem.All(Connection.Db))
            {
                if (!item.PluginName.StartsWith("ConvPert", StringComparison.Ordinal)) continue;
                var flipped = item.AsSortOrderItem().SortIndex == 0 ? 1 : 0;
                tx.Add(item.Id, SortOrderItem.SortIndex, flipped);
            }
            await tx.Commit();
        }

        await variety.ApplyCuratedOrder(loadout.LoadoutId, OneOf<LoadoutId, CollectionGroupId>.FromT0(loadout.LoadoutId), ["ConvPertB.esp", "ConvPertA.esp"]);

        // Re-seeding over perturbed rows must converge in place, not throw or duplicate.
        variety.GetPersistedPluginOrder(loadout.LoadoutId)
            .Should().Equal("ConvPertB.esp", "ConvPertA.esp");
    }
}
