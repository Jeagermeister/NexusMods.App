using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Games.CreationEngine.SortOrder;
using Apocrypha.Games.TestFramework;
using NexusMods.HyperDuck;
using NexusMods.Paths;
using Apocrypha.Sdk.Loadouts;
using Apocrypha.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;
using OneOf;

namespace Apocrypha.Games.CreationEngine.Tests.SortOrder;

/// <summary>
/// Datastore-backed coverage for the plugin sort order: seeding a curated order, reconciling
/// later-installed plugins, and reading it back. No game install, no network.
/// </summary>
public class PluginSortOrderVarietyTests(ITestOutputHelper outputHelper) : AIsolatedGameTest<PluginSortOrderVarietyTests, CreationEngine.Fallout4.Fallout4>(outputHelper)
{
    protected override IServiceCollection AddServices(IServiceCollection services)
    {
        return base.AddServices(services)
            .AddCreationEngine()
            .AddAdapters()
            .AddUniversalGameLocator<CreationEngine.Fallout4.Fallout4>(new Version("1.10.163"));
    }

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
