namespace Apocrypha.Games.CreationEngine.SortOrder;

/// <summary>
/// Deterministic, master-respecting ordering for Creation Engine plugins.
/// </summary>
/// <remarks>
/// The general-purpose <c>ISorter</c> emits every unblocked item per round, which cannot reproduce
/// a curated sequence: an item whose dependencies resolve early is emitted with its whole round,
/// ahead of curated-earlier items still waiting on theirs. The single-pop sort here always emits
/// the single highest-preference unblocked item instead, so the output follows the preference
/// order exactly wherever the hard constraints allow it.
/// </remarks>
public static class LoadOrderSorting
{
    /// <summary>
    /// A plugin to sort: its file name and the file names of its master references.
    /// </summary>
    public readonly record struct PluginNode(string FileName, IReadOnlyList<string> Masters);

    /// <summary>
    /// The block a plugin belongs to in a conventional load order: masters (.esm) ahead of light
    /// plugins (.esl) ahead of regular plugins. A grouping preference, not a dependency — master
    /// references are the only hard ordering rules.
    /// Extension suffixes here mirror <see cref="KnownCEExtensions.PluginFiles"/>.
    /// </summary>
    public static int LoadOrderClassOf(string fileName)
    {
        if (fileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)) return 0;
        if (fileName.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    /// <summary>
    /// Sorts plugins so that every plugin loads after its masters, preferring the curated position
    /// where one is given, then the conventional (class, name) order for plugins the curated data
    /// does not mention. Master references to plugins outside the set are ignored here — missing
    /// masters are a diagnostic concern, not a sorting one.
    /// </summary>
    /// <param name="nodes">The plugins to sort.</param>
    /// <param name="curatedIndex">
    /// Optional preferred position per plugin file name. Must be a case-insensitive dictionary;
    /// plugins absent from it sort after all mentioned ones, by (class, name).
    /// </param>
    /// <param name="cyclePlugins">
    /// File names involved in a master-reference cycle, if any. Cycles only occur with malformed
    /// plugin headers; the affected plugins are still emitted, in preference order, after
    /// everything sortable.
    /// </param>
    /// <returns>Indices into <paramref name="nodes"/> in emit order.</returns>
    public static int[] SortPlugins(
        IReadOnlyList<PluginNode> nodes,
        IReadOnlyDictionary<string, int>? curatedIndex,
        out List<string> cyclePlugins)
    {
        var indexByName = new Dictionary<string, int>(nodes.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            indexByName.TryAdd(nodes[i].FileName, i);
        }

        var dependencies = new List<int>[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var deps = new List<int>();
            foreach (var master in nodes[i].Masters)
            {
                if (indexByName.TryGetValue(master, out var masterIndex) && masterIndex != i)
                    deps.Add(masterIndex);
            }
            dependencies[i] = deps;
        }

        int CuratedOf(int i) => curatedIndex is not null && curatedIndex.TryGetValue(nodes[i].FileName, out var idx) ? idx : int.MaxValue;

        var comparer = Comparer<int>.Create((a, b) =>
        {
            var byCurated = CuratedOf(a).CompareTo(CuratedOf(b));
            if (byCurated != 0) return byCurated;

            var byClass = LoadOrderClassOf(nodes[a].FileName).CompareTo(LoadOrderClassOf(nodes[b].FileName));
            if (byClass != 0) return byClass;

            var byName = string.Compare(nodes[a].FileName, nodes[b].FileName, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.CompareTo(b);
        });

        var sorted = TopoSortByPriority(nodes.Count, i => dependencies[i], comparer, out var cycleNodes);
        cyclePlugins = cycleNodes.Select(i => nodes[i].FileName).ToList();
        return sorted;
    }

    /// <summary>
    /// Single-pop Kahn's algorithm: of all nodes whose dependencies have been emitted, always emit
    /// the one the comparer ranks first. Produces the unique topological order that best matches
    /// the preference order.
    /// </summary>
    /// <param name="count">Number of nodes; nodes are identified by index.</param>
    /// <param name="dependenciesOf">For a node, the nodes that must be emitted before it. Duplicates and self-references are tolerated.</param>
    /// <param name="priority">Preference over node indices; lower ranks emit first among unblocked nodes.</param>
    /// <param name="cycleNodes">
    /// Nodes whose dependencies can never be satisfied: members of a dependency cycle and
    /// everything downstream of one. Instead of throwing, they are appended to the result in
    /// preference order, ignoring the unsatisfiable constraints.
    /// </param>
    /// <returns>All node indices in emit order.</returns>
    public static int[] TopoSortByPriority(
        int count,
        Func<int, IEnumerable<int>> dependenciesOf,
        IComparer<int> priority,
        out List<int> cycleNodes)
    {
        var dependents = new List<int>[count];
        var inDegree = new int[count];
        var seen = new HashSet<int>();

        for (var i = 0; i < count; i++)
        {
            seen.Clear();
            foreach (var dep in dependenciesOf(i))
            {
                if (dep == i || dep < 0 || dep >= count || !seen.Add(dep)) continue;
                (dependents[dep] ??= []).Add(i);
                inDegree[i]++;
            }
        }

        var queue = new PriorityQueue<int, int>(priority);
        for (var i = 0; i < count; i++)
        {
            if (inDegree[i] == 0) queue.Enqueue(i, i);
        }

        var result = new int[count];
        var emitted = 0;
        while (queue.TryDequeue(out var node, out _))
        {
            result[emitted++] = node;
            if (dependents[node] is not { } children) continue;
            foreach (var child in children)
            {
                if (--inDegree[child] == 0) queue.Enqueue(child, child);
            }
        }

        cycleNodes = [];
        if (emitted == count) return result;

        // A cycle: emit the stuck remainder in preference order rather than dropping or throwing.
        for (var i = 0; i < count; i++)
        {
            if (inDegree[i] > 0) cycleNodes.Add(i);
        }
        cycleNodes.Sort(priority);
        foreach (var node in cycleNodes)
        {
            result[emitted++] = node;
        }
        return result;
    }
}
