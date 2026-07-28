using FluentAssertions;
using Apocrypha.Abstractions.Collections.Json;
using Apocrypha.Games.CreationEngine.SortOrder;

namespace Apocrypha.Games.CreationEngine.Tests.SortOrder;

/// <summary>
/// Pure-function coverage for resolving a collection's `plugins` + `pluginRules` into a total
/// order. No game install, no network, no database.
/// </summary>
public class CuratedLoadOrderTests
{
    private static CollectionPlugin[] Plugins(params string[] names) =>
        names.Select(name => new CollectionPlugin { Name = name }).ToArray();

    [Fact]
    public void ManifestOrderIsTheBaseline()
    {
        var result = CuratedLoadOrder.Resolve(Plugins("B.esp", "A.esp", "C.esm"), rules: null);

        // No rules: the manifest array is the curated order, never re-sorted by class or name.
        result.Should().Equal("B.esp", "A.esp", "C.esm");
    }

    [Fact]
    public void DisabledAndDuplicatePluginsAreDropped()
    {
        CollectionPlugin[] plugins =
        [
            new() { Name = "A.esp" },
            new() { Name = "Gone.esp", Enabled = false },
            new() { Name = "a.esp" }, // same plugin, different casing
            new() { Name = "B.esp", Enabled = true },
        ];

        var result = CuratedLoadOrder.Resolve(plugins, rules: null);

        result.Should().Equal("A.esp", "B.esp");
    }

    [Fact]
    public void AfterConstraintOverridesManifestOrder()
    {
        var rules = new GamebryoPluginRules
        {
            Plugins = [new UserlistEntry { Name = "A.esp", After = ["C.esp"] }],
        };

        var result = CuratedLoadOrder.Resolve(Plugins("A.esp", "B.esp", "C.esp"), rules);

        // A must follow C; B keeps its manifest position.
        result.Should().Equal("B.esp", "C.esp", "A.esp");
    }

    [Fact]
    public void GroupOrderingAppliesToMembers()
    {
        var rules = new GamebryoPluginRules
        {
            Groups =
            [
                new UserlistEntry { Name = "Patches", After = ["default"] },
            ],
            Plugins =
            [
                new UserlistEntry { Name = "Patch.esp", Group = "Patches" },
            ],
        };

        // The patch sits first in the manifest, but its group loads after the (implicit) default
        // group every other plugin belongs to.
        var result = CuratedLoadOrder.Resolve(Plugins("Patch.esp", "Core.esm", "Extra.esp"), rules);

        result.Should().Equal("Core.esm", "Extra.esp", "Patch.esp");
    }

    [Fact]
    public void UnresolvableReferencesAreSkipped()
    {
        var warnings = new List<string>();
        var rules = new GamebryoPluginRules
        {
            Groups = [new UserlistEntry { Name = "Late", After = ["Some LOOT Masterlist Group"] }],
            Plugins =
            [
                new UserlistEntry { Name = "A.esp", After = ["NotInstalled.esp"], Group = "Undeclared Group" },
                new UserlistEntry { Name = "NotCurated.esp", After = ["A.esp"] },
            ],
        };

        var result = CuratedLoadOrder.Resolve(Plugins("A.esp", "B.esp"), rules, warnings.Add);

        result.Should().Equal("A.esp", "B.esp");
        warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void CyclesDegradeToManifestOrderInsteadOfThrowing()
    {
        var warnings = new List<string>();
        var rules = new GamebryoPluginRules
        {
            Plugins =
            [
                new UserlistEntry { Name = "A.esp", After = ["B.esp"] },
                new UserlistEntry { Name = "B.esp", After = ["A.esp"] },
            ],
        };

        var result = CuratedLoadOrder.Resolve(Plugins("A.esp", "B.esp", "C.esp"), rules, warnings.Add);

        // Everything is still emitted, and the manifest order breaks the tie.
        result.Should().Equal("C.esp", "A.esp", "B.esp");
        warnings.Should().Contain(warning => warning.Contains("cycle"));
    }

    [Fact]
    public void GroupCycleDegradesButPluginRulesStillApply()
    {
        var warnings = new List<string>();
        var rules = new GamebryoPluginRules
        {
            Groups =
            [
                new UserlistEntry { Name = "One", After = ["Two"] },
                new UserlistEntry { Name = "Two", After = ["One"] },
            ],
            Plugins = [new UserlistEntry { Name = "A.esp", After = ["B.esp"] }],
        };

        var result = CuratedLoadOrder.Resolve(Plugins("A.esp", "B.esp"), rules, warnings.Add);

        result.Should().Equal("B.esp", "A.esp");
        warnings.Should().Contain(warning => warning.Contains("cycle"));
    }

    [Fact]
    public void EmptyManifestResolvesEmpty()
    {
        CuratedLoadOrder.Resolve([], rules: null).Should().BeEmpty();
        CuratedLoadOrder.Resolve([new CollectionPlugin { Name = "A.esp", Enabled = false }], rules: null).Should().BeEmpty();
    }
}
