using NexusMods.Paths;

namespace Apocrypha.Sdk.Jobs;

/// <summary>
/// Stream wrapper for progress reporting.
/// </summary>
public sealed class StreamProgressWrapper<TState> : Stream
{
    private readonly Stream _innerStream;

    private readonly TState _state;
    private readonly Action<TState, (Size, double)> _notifyWritten;
    private readonly ITimer _timer;

    /// <summary>
    /// Constructor.
    /// </summary>
    public StreamProgressWrapper(
        Stream innerStream,
        TState state,
        Action<TState, (Size, double)> notifyWritten,
        TimeSpan period = default,
        TimeProvider? timeProvider = null)
    {
        _innerStream = innerStream;
        _state = state;
        _notifyWritten = notifyWritten;

        _period = period == default(TimeSpan) ? TimeSpan.FromSeconds(1) : period;
        timeProvider ??= TimeProvider.System;

        // The counters MUST be initialized before the timer exists: with dueTime zero the first
        // NotifyLoop can fire on a pool thread before the constructor's next line runs, and it
        // then reports the default value -- zero -- to the callback. HttpDownloadJob's callback
        // persists that as TotalBytesDownloaded, wiping real resume progress, so its
        // 200-with-partial-progress reset never engages and the full body lands after the stale
        // prefix: silent corruption with a plausible file size. Load-dependent -- caught by
        // ResumesAfterATruncatedConnectionOnAServerWithoutRanges failing in CI while green
        // locally, and the likely mechanism behind ledger item 19c's original one-time failure.
        var pos = Size.FromLong(_innerStream.Position);
        _currentBytesWritten = pos;
        _lastBytesWritten = pos;

        _timer = timeProvider.CreateTimer(NotifyLoop, state: this, dueTime: TimeSpan.Zero, period: _period);
    }

    private readonly TimeSpan _period;
    private Size _lastBytesWritten;
    private Size _currentBytesWritten;

    private static void NotifyLoop(object? state)
    {
        if (state is not StreamProgressWrapper<TState> self) return;

        var current = self._currentBytesWritten;

        var diff = current - self._lastBytesWritten;
        var period = self._period;
        var speed = diff.Value / period.TotalSeconds;

        self._lastBytesWritten = self._currentBytesWritten;
        self._notifyWritten.Invoke(self._state, (current, speed));
    }

    private void SetBytesWritten(long count)
    {
        _currentBytesWritten = Size.FromLong(count);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        _innerStream.Flush();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return _innerStream.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return _innerStream.Seek(offset, origin);
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        _innerStream.SetLength(value);
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        _innerStream.Write(buffer, offset, count);
        SetBytesWritten(_innerStream.Position);
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _innerStream.WriteAsync(buffer, cancellationToken);
        SetBytesWritten(_innerStream.Position);
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _innerStream.Write(buffer);
        SetBytesWritten(_innerStream.Position);
    }

    /// <inheritdoc/>
    public override bool CanRead => _innerStream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => _innerStream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => _innerStream.CanWrite;

    /// <inheritdoc/>
    public override long Length => _innerStream.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await _innerStream.DisposeAsync();
        await _timer.DisposeAsync();
    }
}
