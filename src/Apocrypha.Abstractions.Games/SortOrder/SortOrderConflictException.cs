namespace Apocrypha.Abstractions.Games;

/// <summary>
/// Thrown by the sort-order compare-and-swap when another transaction has moved the sort order
/// since the caller read it. This is an ordinary lost race, not a fault: the caller is expected to
/// re-read and retry.
/// </summary>
/// <remarks>
/// It exists as its own type so the retry path can recognise a lost race precisely. The previous
/// code threw a bare <see cref="InvalidOperationException"/> and caught that exact type, which
/// meant any wrapping by the datastore turned a retryable conflict into an escaping exception --
/// and made a genuine <see cref="InvalidOperationException"/> from elsewhere look like a conflict.
/// </remarks>
public sealed class SortOrderConflictException : InvalidOperationException
{
    public SortOrderConflictException(string message) : base(message) { }

    /// <summary>
    /// Returns true if <paramref name="exception"/> is a conflict, or wraps one at any depth.
    /// </summary>
    public static bool IsConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SortOrderConflictException) return true;

            if (current is AggregateException aggregate &&
                aggregate.InnerExceptions.Any(IsConflict))
            {
                return true;
            }
        }

        return false;
    }
}
