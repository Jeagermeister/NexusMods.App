using NexusMods.MnemonicDB.Abstractions;
using R3;

namespace Apocrypha.Abstractions.Games;

/// <summary>
/// One sort order that needs reconciling: a loadout, optionally narrowed to a collection group
/// inside it. <see cref="CollectionId"/> is <c>0</c> for the loadout-level order, matching the
/// convention the change query itself uses.
/// </summary>
internal readonly record struct ReconcileTarget(EntityId LoadoutId, EntityId CollectionId)
{
    public bool IsCollectionLevel => CollectionId != 0;
}

/// <summary>
/// Collapses a burst of loadout-item changes into the smallest set of sort-order reconciliations
/// that produces the same result.
///
/// <para>
/// Reconciliation is a <b>full</b> recomputation against the newest database, so it is idempotent
/// and the only thing that matters is that one runs after the last change. Without batching we ran
/// one per commit, and a collection install is thousands of commits — measured at ~582 ms per
/// reconcile (cold) / ~282 ms (warm) on a 682-plugin, 908-mod Fallout 4 loadout, which is minutes
/// of redundant work for a single install.
/// </para>
///
/// <para>
/// The window deliberately <b>accumulates rather than drops</b>. A debounce that kept only the
/// latest changeset would lose the loadout ids carried by the ones it discarded, and those
/// loadouts would never be reconciled at all.
/// </para>
/// </summary>
internal static class SortOrderReconcileBatching
{
    /// <summary>
    /// How long changes are gathered before reconciling. Long enough to swallow a commit storm,
    /// short enough to be invisible on the paths a user watches: this observable fires on item
    /// add/remove (installs and uninstalls), never on the drag-to-reorder path, which commits
    /// through <c>ASortOrderVariety</c> directly.
    /// </summary>
    internal static readonly TimeSpan ReconcileWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gathers targets over <paramref name="window"/> and emits each distinct one once, ordered
    /// loadout-level first so a collection's parent loadout is reconciled before the collection
    /// (the order the un-batched code ran them in). Windows that gathered nothing emit nothing.
    /// </summary>
    public static Observable<ReconcileTarget[]> Batch(
        Observable<IReadOnlyList<ReconcileTarget>> source,
        TimeSpan window,
        TimeProvider timeProvider)
    {
        return source
            .Where(static targets => targets.Count > 0)
            .Chunk(window, timeProvider)
            .Select(static gathered => Coalesce(gathered))
            .Where(static targets => targets.Length > 0);
    }

    /// <summary>
    /// Flattens the window's targets to a distinct, loadout-level-first list.
    /// </summary>
    public static ReconcileTarget[] Coalesce(IReadOnlyList<IReadOnlyList<ReconcileTarget>> gathered)
    {
        if (gathered.Count == 0) return [];

        var seen = new HashSet<ReconcileTarget>();
        var loadoutLevel = new List<ReconcileTarget>();
        var collectionLevel = new List<ReconcileTarget>();

        foreach (var targets in gathered)
        {
            foreach (var target in targets)
            {
                if (!seen.Add(target)) continue;
                (target.IsCollectionLevel ? collectionLevel : loadoutLevel).Add(target);
            }
        }

        if (collectionLevel.Count == 0) return loadoutLevel.ToArray();

        var result = new ReconcileTarget[loadoutLevel.Count + collectionLevel.Count];
        loadoutLevel.CopyTo(result, 0);
        collectionLevel.CopyTo(result, loadoutLevel.Count);
        return result;
    }
}
