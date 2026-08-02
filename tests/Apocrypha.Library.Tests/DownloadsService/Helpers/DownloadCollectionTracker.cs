using DynamicData;
using Apocrypha.Abstractions.Downloads;

namespace Apocrypha.Library.Tests.DownloadsService.Helpers;

/// <summary>
/// Tracks one of the download service's keyed change streams and lets a test await a count
/// instead of polling for it (deferred-work ledger item 19b).
///
/// <para>
/// The plumbing this replaces kept a <c>List</c> and appended on <em>both</em> Add and Update,
/// so a status or progress <c>Update</c> landing in the same batch as the <c>Add</c> jumped the
/// count straight from 0 to 2 — and a poll for <c>Count == 1</c> then ran its full 30-second
/// timeout while the isolated run, where no Update raced the poll, passed in milliseconds.
/// That is exactly the load-sensitivity observed in CI's Clean Environment lane. Keying by
/// <see cref="DownloadId"/> makes an Update a replacement, which is what it is; completing
/// waiters from inside the subscription makes the wait signal-driven, with the timeout only as
/// a failure backstop rather than the synchronization mechanism.
/// </para>
/// </summary>
public sealed class DownloadCollectionTracker : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<DownloadId, DownloadInfo> _items = new();
    private readonly List<(int Count, TaskCompletionSource<bool> Tcs)> _waiters = [];
    private readonly IDisposable _subscription;

    public DownloadCollectionTracker(IObservable<IChangeSet<DownloadInfo, DownloadId>> source)
    {
        _subscription = source.Subscribe(changes =>
        {
            lock (_lock)
            {
                foreach (var change in changes)
                {
                    switch (change.Reason)
                    {
                        case ChangeReason.Add:
                        case ChangeReason.Update:
                        case ChangeReason.Refresh:
                            _items[change.Key] = change.Current;
                            break;
                        case ChangeReason.Remove:
                            _items.Remove(change.Key);
                            break;
                    }
                }

                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_waiters[i].Count != _items.Count) continue;
                    _waiters[i].Tcs.TrySetResult(true);
                    _waiters.RemoveAt(i);
                }
            }
        });
    }

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    /// <summary>
    /// Completes when the tracked collection reaches <paramref name="expected"/> items — either
    /// immediately, or from the change batch that gets it there. Returns false only if the
    /// backstop timeout elapses first.
    /// </summary>
    public async Task<bool> WaitForCount(int expected, TimeSpan timeout)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            if (_items.Count == expected) return true;
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((expected, tcs));
        }

        using var cts = new CancellationTokenSource(timeout);
        await using var registration = cts.Token.Register(() => tcs.TrySetResult(false));
        return await tcs.Task;
    }

    public void Dispose() => _subscription.Dispose();
}
