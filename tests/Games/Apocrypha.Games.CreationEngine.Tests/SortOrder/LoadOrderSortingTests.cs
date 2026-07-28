using FluentAssertions;
using Apocrypha.Games.CreationEngine.SortOrder;

namespace Apocrypha.Games.CreationEngine.Tests.SortOrder;

/// <summary>
/// Pure-function coverage for the master-respecting plugin sort behind plugins.txt.
/// </summary>
public class LoadOrderSortingTests
{
    private static LoadOrderSorting.PluginNode Node(string name, params string[] masters) => new(name, masters);

    private static string[] Sort(IReadOnlyList<LoadOrderSorting.PluginNode> nodes, IReadOnlyDictionary<string, int>? curated = null)
    {
        var order = LoadOrderSorting.SortPlugins(nodes, curated, out var cyclePlugins);
        cyclePlugins.Should().BeEmpty();
        return order.Select(i => nodes[i].FileName).ToArray();
    }

    private static Dictionary<string, int> Curated(params string[] names) =>
        names.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void WithoutCuratedData_MastersFormALeadingBlock()
    {
        // The upstream behaviour this replaces put ESPs whose only master is the base game ahead
        // of DLC masters; mainstream tools emit all masters first. See the design doc.
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("LooksMenu.esp", "Fallout4.esm"),
            Node("Fallout4.esm"),
            Node("DLCCoast.esm", "Fallout4.esm"),
            Node("SomeLight.esl", "Fallout4.esm"),
        ];

        Sort(nodes).Should().Equal("Fallout4.esm", "DLCCoast.esm", "SomeLight.esl", "LooksMenu.esp");
    }

    [Fact]
    public void CuratedOrderWinsOverClassAndName()
    {
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("Alpha.esp"),
            Node("Bravo.esp"),
            Node("Zulu.esm"),
        ];

        // Curated wants an ESP ahead of an ESM and reverse-alphabetical ESPs; nothing constrains
        // it, so it wins exactly.
        Sort(nodes, Curated("bravo.esp", "ALPHA.ESP", "Zulu.esm"))
            .Should().Equal("Bravo.esp", "Alpha.esp", "Zulu.esm");
    }

    [Fact]
    public void MasterReferencesOverrideCuratedOrder()
    {
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("Patch.esp", "Core.esm"),
            Node("Core.esm"),
        ];

        // The curator asks for the patch first; its master still loads before it.
        Sort(nodes, Curated("Patch.esp", "Core.esm")).Should().Equal("Core.esm", "Patch.esp");
    }

    [Fact]
    public void UncuratedPluginsAppendAfterCuratedOnes()
    {
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("Extra.esm"),
            Node("Curated.esp"),
        ];

        // The uncurated ESM sorts after the curated ESP: persisted preference beats class.
        Sort(nodes, Curated("Curated.esp")).Should().Equal("Curated.esp", "Extra.esm");
    }

    [Fact]
    public void CuratedFidelityHoldsAcrossDependencyDepths()
    {
        // The round-based sorter emitted whole dependency tiers at once, so a curated-late but
        // dependency-free plugin jumped ahead of a curated-early plugin with masters. The
        // single-pop sort must not.
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("Big.esm"),
            Node("X.esp", "Big.esm"),
            Node("Y.esp"),
        ];

        Sort(nodes, Curated("Big.esm", "X.esp", "Y.esp")).Should().Equal("Big.esm", "X.esp", "Y.esp");
    }

    [Fact]
    public void MissingMastersDoNotBlockSorting()
    {
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("Orphan.esp", "NotInstalled.esm"),
            Node("Base.esm"),
        ];

        Sort(nodes).Should().Equal("Base.esm", "Orphan.esp");
    }

    [Fact]
    public void MasterCyclesDegradeInsteadOfThrowing()
    {
        // Only malformed headers can do this; everything must still be written.
        LoadOrderSorting.PluginNode[] nodes =
        [
            Node("A.esp", "B.esp"),
            Node("B.esp", "A.esp"),
            Node("Base.esm"),
        ];

        var order = LoadOrderSorting.SortPlugins(nodes, null, out var cyclePlugins);

        order.Select(i => nodes[i].FileName).Should().Equal("Base.esm", "A.esp", "B.esp");
        cyclePlugins.Should().BeEquivalentTo("A.esp", "B.esp");
    }

    [Fact]
    public void OutputIsDeterministicForUnrelatedPlugins()
    {
        var nodes = new[] { Node("c.esp"), Node("A.esp"), Node("b.esp") };

        Sort(nodes).Should().Equal("A.esp", "b.esp", "c.esp");
        Sort(nodes.Reverse().ToArray()).Should().Equal("A.esp", "b.esp", "c.esp");
    }
}
