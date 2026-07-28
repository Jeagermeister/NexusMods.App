using Apocrypha.Abstractions.Collections.Json;

namespace Apocrypha.Games.CreationEngine.SortOrder;

/// <summary>
/// Resolves a collection's curated plugin data (<c>plugins</c> + <c>pluginRules</c>) into a single
/// total load order.
/// </summary>
/// <remarks>
/// The manifest's <c>plugins</c> array is the curator's enabled set in their intended order; the
/// LOOT-style <c>pluginRules</c> add per-plugin <c>after</c> constraints and a group graph on top.
/// The array order is used as the base preference and the rules as hard constraints, so a manifest
/// whose array already satisfies its own rules resolves to exactly the array order. Curator data
/// is external input: unresolvable references are skipped and cycles degrade to the base
/// preference with a warning — resolving never throws.
/// </remarks>
public static class CuratedLoadOrder
{
    private const string DefaultGroup = "default";

    /// <summary>
    /// Resolves the curated order. Returns plugin file names in load order — plugins the curator
    /// has disabled are excluded entirely. Empty when the collection carries no plugin data (the
    /// case for every non-Gamebryo game).
    /// </summary>
    /// <param name="plugins">The manifest's <c>plugins</c> array, in manifest order.</param>
    /// <param name="rules">The manifest's <c>pluginRules</c>, if present.</param>
    /// <param name="warn">Receives human-readable warnings about degraded curator data.</param>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<CollectionPlugin> plugins,
        GamebryoPluginRules? rules,
        Action<string>? warn = null)
    {
        warn ??= static _ => { };

        // The curator's enabled set, in manifest order. Plugin file names are case-insensitive
        // identifiers; the first occurrence wins on duplicates.
        var universe = new List<string>();
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            if (plugin.Enabled == false) continue;
            if (string.IsNullOrWhiteSpace(plugin.Name)) continue;
            if (indexByName.TryAdd(plugin.Name, universe.Count))
                universe.Add(plugin.Name);
        }

        if (universe.Count == 0) return [];
        if (rules is null || (rules.Plugins.Length == 0 && rules.Groups.Length == 0))
            return universe;

        var groupRules = ResolveGroupGraph(rules, warn);
        var dependencies = BuildPluginDependencies(rules, universe, indexByName, groupRules, warn);

        // Base preference is the manifest order itself, so the priority over node indices is just
        // the numeric order — TopoSortByPriority deviates from it only where a constraint forces it.
        var sorted = LoadOrderSorting.TopoSortByPriority(
            universe.Count,
            i => dependencies[i],
            Comparer<int>.Default,
            out var cycleNodes);

        if (cycleNodes.Count > 0)
        {
            warn($"Curated plugin rules contain an ordering cycle involving {string.Join(", ", cycleNodes.Take(5).Select(i => universe[i]))}" +
                 $"{(cycleNodes.Count > 5 ? $" and {cycleNodes.Count - 5} more" : string.Empty)}; falling back to manifest order for those plugins");
        }

        return sorted.Select(i => universe[i]).ToList();
    }

    /// <summary>
    /// Topologically orders the declared groups by their <c>after</c> edges and computes, for each
    /// group, the set of groups it (transitively) loads after. The implicit LOOT <c>default</c>
    /// group always exists and is preferred first among unconstrained groups, so ungrouped plugins
    /// lead and the typical patch groups follow.
    /// </summary>
    private static GroupGraph ResolveGroupGraph(GamebryoPluginRules rules, Action<string> warn)
    {
        var names = new List<string> { DefaultGroup };
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [DefaultGroup] = 0 };
        foreach (var group in rules.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name)) continue;
            if (indexByName.TryAdd(group.Name, names.Count))
                names.Add(group.Name);
        }

        var dependencies = new List<int>[names.Count];
        for (var i = 0; i < names.Count; i++) dependencies[i] = [];

        var unknownRefs = 0;
        foreach (var group in rules.Groups)
        {
            if (!indexByName.TryGetValue(group.Name, out var groupIndex)) continue;
            foreach (var after in group.After)
            {
                // Groups from LOOT's masterlist (not declared by the collection) can appear here;
                // we have no position for them, so the edge is dropped.
                if (indexByName.TryGetValue(after, out var afterIndex))
                    dependencies[groupIndex].Add(afterIndex);
                else
                    unknownRefs++;
            }
        }
        if (unknownRefs > 0)
            warn($"{unknownRefs} group 'after' reference(s) point at groups the collection does not declare; skipped");

        var order = LoadOrderSorting.TopoSortByPriority(names.Count, i => dependencies[i], Comparer<int>.Default, out var cycleNodes);
        if (cycleNodes.Count > 0)
            warn($"Curated plugin groups contain an ordering cycle involving {string.Join(", ", cycleNodes.Select(i => names[i]))}; group order degraded to declaration order");

        // Transitive closure over the (small) group DAG: loadsAfter[g] = every group g must follow.
        var loadsAfter = new HashSet<int>[names.Count];
        foreach (var groupIndex in order)
        {
            var closure = new HashSet<int>();
            foreach (var dep in dependencies[groupIndex])
            {
                if (closure.Add(dep) && loadsAfter[dep] is { } transitive)
                    closure.UnionWith(transitive);
            }
            loadsAfter[groupIndex] = closure;
        }

        return new GroupGraph(indexByName, loadsAfter);
    }

    /// <summary>
    /// Builds per-plugin dependency lists from the per-plugin <c>after</c> constraints plus the
    /// group graph (every member of a group loads after every member of each group it follows).
    /// </summary>
    private static List<int>[] BuildPluginDependencies(
        GamebryoPluginRules rules,
        List<string> universe,
        Dictionary<string, int> indexByName,
        GroupGraph groups,
        Action<string> warn)
    {
        var dependencies = new List<int>[universe.Count];
        for (var i = 0; i < universe.Count; i++) dependencies[i] = [];

        // Which group each plugin belongs to; ungrouped plugins are in `default`.
        var groupOfPlugin = new int[universe.Count];
        var unknownGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedAfters = 0;

        foreach (var entry in rules.Plugins)
        {
            if (!indexByName.TryGetValue(entry.Name, out var pluginIndex)) continue;

            if (entry.Group is not null)
            {
                if (groups.IndexByName.TryGetValue(entry.Group, out var groupIndex))
                    groupOfPlugin[pluginIndex] = groupIndex;
                else
                    unknownGroups.Add(entry.Group);
            }

            foreach (var after in entry.After)
            {
                if (indexByName.TryGetValue(after, out var afterIndex))
                {
                    if (afterIndex != pluginIndex) dependencies[pluginIndex].Add(afterIndex);
                }
                else
                {
                    // Not installed, curator-disabled, or a conditional LOOT reference for a
                    // different setup — there is nothing to order against.
                    unresolvedAfters++;
                }
            }
        }

        if (unknownGroups.Count > 0)
            warn($"Plugin group(s) {string.Join(", ", unknownGroups.Take(5))} are not declared by the collection; treating their plugins as ungrouped");
        if (unresolvedAfters > 0)
            warn($"{unresolvedAfters} plugin 'after' reference(s) don't resolve to a curated plugin; skipped");

        // Group-derived edges: members of a group load after all members of every group it follows.
        var membersOfGroup = new List<int>[groups.LoadsAfter.Length];
        for (var i = 0; i < membersOfGroup.Length; i++) membersOfGroup[i] = [];
        for (var i = 0; i < universe.Count; i++) membersOfGroup[groupOfPlugin[i]].Add(i);

        for (var groupIndex = 0; groupIndex < membersOfGroup.Length; groupIndex++)
        {
            if (membersOfGroup[groupIndex].Count == 0 || groups.LoadsAfter[groupIndex].Count == 0) continue;
            foreach (var earlierGroup in groups.LoadsAfter[groupIndex])
            {
                foreach (var member in membersOfGroup[groupIndex])
                {
                    dependencies[member].AddRange(membersOfGroup[earlierGroup]);
                }
            }
        }

        return dependencies;
    }

    private sealed record GroupGraph(Dictionary<string, int> IndexByName, HashSet<int>[] LoadsAfter);
}
