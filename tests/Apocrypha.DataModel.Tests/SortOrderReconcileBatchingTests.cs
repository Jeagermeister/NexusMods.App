using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NexusMods.MnemonicDB.Abstractions;
using Apocrypha.Abstractions.Games;
using R3;

namespace Apocrypha.DataModel.Tests;

/// <summary>
/// The batching that keeps a collection install from firing one full sort-order reconciliation per
/// commit. These pin the two properties the optimisation rests on: a burst collapses to one
/// reconcile per distinct target, and the last change in a burst always gets reconciled.
/// </summary>
public class SortOrderReconcileBatchingTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    private static EntityId Id(ulong value) => EntityId.From(value);
    private static ReconcileTarget Loadout(ulong loadoutId) => new(Id(loadoutId), Id(0));
    private static ReconcileTarget Collection(ulong loadoutId, ulong collectionId) => new(Id(loadoutId), Id(collectionId));

    /// <summary>
    /// The whole point: a storm of commits touching one loadout must produce exactly one
    /// reconciliation, not one per commit.
    /// </summary>
    [Fact]
    public void ABurstOfChangesToOneLoadoutCollapsesToASingleReconcile()
    {
        var time = new FakeTimeProvider();
        var source = new Subject<IReadOnlyList<ReconcileTarget>>();
        var emitted = new List<ReconcileTarget[]>();

        using var _ = SortOrderReconcileBatching.Batch(source, Window, time).Subscribe(emitted.Add);

        // 1,000 commits, all inside the window, all for the same loadout.
        for (var i = 0; i < 1000; i++)
            source.OnNext([Loadout(42)]);

        emitted.Should().BeEmpty("nothing should reconcile until the window closes");

        time.Advance(Window);

        emitted.Should().ContainSingle();
        emitted[0].Should().Equal(Loadout(42));
    }

    /// <summary>
    /// Batching must not lose targets the way a plain debounce would: every loadout touched during
    /// the window gets reconciled, not just the one that happened to arrive last.
    /// </summary>
    [Fact]
    public void EveryDistinctTargetInTheWindowIsReconciledOnce()
    {
        var time = new FakeTimeProvider();
        var source = new Subject<IReadOnlyList<ReconcileTarget>>();
        var emitted = new List<ReconcileTarget[]>();

        using var _ = SortOrderReconcileBatching.Batch(source, Window, time).Subscribe(emitted.Add);

        source.OnNext([Loadout(1)]);
        source.OnNext([Loadout(2), Collection(2, 20)]);
        source.OnNext([Loadout(1)]);
        source.OnNext([Loadout(3)]);

        time.Advance(Window);

        emitted.Should().ContainSingle();
        emitted[0].Should().HaveCount(4);
        emitted[0].Should().Contain(Loadout(1))
            .And.Contain(Loadout(2))
            .And.Contain(Loadout(3))
            .And.Contain(Collection(2, 20));
    }

    /// <summary>
    /// A collection's parent loadout is reconciled before the collection itself, which is the order
    /// the un-batched code ran them in.
    /// </summary>
    [Fact]
    public void LoadoutLevelTargetsComeBeforeCollectionLevelOnes()
    {
        var time = new FakeTimeProvider();
        var source = new Subject<IReadOnlyList<ReconcileTarget>>();
        var emitted = new List<ReconcileTarget[]>();

        using var _ = SortOrderReconcileBatching.Batch(source, Window, time).Subscribe(emitted.Add);

        source.OnNext([Collection(7, 70), Loadout(7)]);
        time.Advance(Window);

        emitted.Should().ContainSingle();
        emitted[0].Should().Equal(Loadout(7), Collection(7, 70));
    }

    /// <summary>
    /// The correctness backstop for any time-based batching: a change that arrives with nothing
    /// after it must still be reconciled once the window elapses. If this fails, an install's last
    /// mod could sit unreconciled indefinitely.
    /// </summary>
    [Fact]
    public void TheFinalChangeInABurstIsStillReconciled()
    {
        var time = new FakeTimeProvider();
        var source = new Subject<IReadOnlyList<ReconcileTarget>>();
        var emitted = new List<ReconcileTarget[]>();

        using var _ = SortOrderReconcileBatching.Batch(source, Window, time).Subscribe(emitted.Add);

        source.OnNext([Loadout(1)]);
        time.Advance(Window);
        emitted.Should().ContainSingle();

        // Quiet for a long while, then one lone straggler.
        time.Advance(TimeSpan.FromSeconds(30));
        source.OnNext([Loadout(2)]);
        time.Advance(Window);

        emitted.Should().HaveCount(2);
        emitted[1].Should().Equal(Loadout(2));
    }

    /// <summary>
    /// Changes in separate windows reconcile separately — batching bounds staleness to one window,
    /// it does not coalesce across an idle period.
    /// </summary>
    [Fact]
    public void ChangesInLaterWindowsReconcileInTheirOwnBatch()
    {
        var time = new FakeTimeProvider();
        var source = new Subject<IReadOnlyList<ReconcileTarget>>();
        var emitted = new List<ReconcileTarget[]>();

        using var _ = SortOrderReconcileBatching.Batch(source, Window, time).Subscribe(emitted.Add);

        source.OnNext([Loadout(1)]);
        time.Advance(Window);
        source.OnNext([Loadout(1)]);
        time.Advance(Window);

        emitted.Should().HaveCount(2);
        emitted[0].Should().Equal(Loadout(1));
        // A change after the window closed is a new batch, not a duplicate suppressed forever.
        emitted[1].Should().Equal(Loadout(1));
    }

    /// <summary>
    /// An idle window must not wake the reconciler up. Update-only changesets extract to an empty
    /// target list upstream, so this also covers "a commit that touched nothing relevant".
    /// </summary>
    [Fact]
    public void IdleAndEmptyWindowsEmitNothing()
    {
        var time = new FakeTimeProvider();
        var source = new Subject<IReadOnlyList<ReconcileTarget>>();
        var emitted = new List<ReconcileTarget[]>();

        using var _ = SortOrderReconcileBatching.Batch(source, Window, time).Subscribe(emitted.Add);

        time.Advance(TimeSpan.FromSeconds(10));
        source.OnNext([]);
        time.Advance(TimeSpan.FromSeconds(10));

        emitted.Should().BeEmpty();
    }

    /// <summary>
    /// Coalescing is order-preserving and deduplicating in isolation, independent of any timing.
    /// </summary>
    [Fact]
    public void CoalesceDedupesWhilePreservingFirstSeenOrder()
    {
        var result = SortOrderReconcileBatching.Coalesce([
            [Loadout(1), Loadout(2)],
            [Loadout(2), Loadout(1), Loadout(3)],
        ]);

        result.Should().Equal(Loadout(1), Loadout(2), Loadout(3));
    }

    [Fact]
    public void CoalesceOfNothingIsEmpty()
    {
        SortOrderReconcileBatching.Coalesce([]).Should().BeEmpty();
        SortOrderReconcileBatching.Coalesce([[], []]).Should().BeEmpty();
    }
}
