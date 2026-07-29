using FluentAssertions;
using Apocrypha.Abstractions.Games;

namespace Apocrypha.Games.RedEngine.Tests;

/// <summary>
/// Pins the one thing that decides whether a lost sort-order race is retried or escapes: the
/// retry path recognises a conflict only through <see cref="SortOrderConflictException.IsConflict"/>,
/// and the datastore is free to wrap whatever a transaction function throws. If unwrapping stops
/// working, reconciliation silently stops retrying and curated load orders go stale.
/// </summary>
public class SortOrderConflictExceptionTests
{
    [Fact]
    public void RecognisesADirectConflict()
    {
        SortOrderConflictException.IsConflict(new SortOrderConflictException("changed")).Should().BeTrue();
    }

    [Fact]
    public void RecognisesAWrappedConflict()
    {
        var wrapped = new InvalidOperationException("Failed to apply transaction functions",
            new SortOrderConflictException("changed"));

        SortOrderConflictException.IsConflict(wrapped).Should().BeTrue();
    }

    [Fact]
    public void RecognisesAConflictInsideAnAggregate()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("unrelated"),
            new SortOrderConflictException("changed"));

        SortOrderConflictException.IsConflict(aggregate).Should().BeTrue();
    }

    [Fact]
    public void DoesNotTreatAnUnrelatedFailureAsAConflict()
    {
        // The previous code caught InvalidOperationException by type, so a genuine bug surfacing as
        // one was swallowed and retried as though it were contention.
        SortOrderConflictException.IsConflict(new InvalidOperationException("something actually broken"))
            .Should().BeFalse();
        SortOrderConflictException.IsConflict(null).Should().BeFalse();
    }
}
