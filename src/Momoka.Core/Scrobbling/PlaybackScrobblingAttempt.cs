namespace Momoka.Scrobbling;

/// <summary>Two monotonic transitions, serialized away from the player event thread.</summary>
public sealed class PlaybackScrobblingAttempt
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly double _completionSeconds;
    private readonly TimeSpan _syncTimeout;
    private readonly Func<bool> _isAllowed;
    private readonly Func<bool, CancellationToken, Task> _sync;
    private readonly Action<string>? _failed;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _cancellation;
    private Task _tail = Task.CompletedTask;
    private Task? _finish;
    private bool _started;
    private bool _startQueued;
    private bool _completionQueued;
    private bool _closed;

    public PlaybackScrobblingAttempt(long durationTicks, int percent, Func<bool> isAllowed,
        Func<bool, CancellationToken, Task> sync, CancellationToken sourceLifetime, Action<string>? failed = null,
        TimeSpan? syncTimeout = null, CancellationToken additionalLifetime = default)
    {
        _completionSeconds = durationTicks > 0
            ? durationTicks / (double)TimeSpan.TicksPerSecond * Math.Clamp(percent, 50, 100) / 100
            : double.PositiveInfinity;
        _isAllowed = isAllowed;
        _sync = sync;
        _failed = failed;
        _syncTimeout = syncTimeout is { } requested && requested > TimeSpan.Zero ? requested : Timeout;
        _lifetime = additionalLifetime.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(sourceLifetime, additionalLifetime)
            : CancellationTokenSource.CreateLinkedTokenSource(sourceLifetime);
        _cancellation = _lifetime.Token;
    }

    public void Observe(double? seconds, bool started)
    {
        lock (_gate)
        {
            if (!_closed) ObserveLocked(seconds, started);
        }
    }

    public Task FinishAsync(double? realPosition, bool started)
    {
        lock (_gate)
        {
            if (_finish is not null) return _finish;
            ObserveLocked(realPosition, started);
            _closed = true;
            _lifetime.CancelAfter(Timeout);
            return _finish = FinishCoreAsync(_tail);
        }
    }

    private void ObserveLocked(double? seconds, bool started)
    {
        _started |= started;
        if (!_started) return;
        if (!_startQueued)
        {
            _startQueued = true;
            QueueLocked(false);
        }
        if (!_completionQueued && seconds is { } position && double.IsFinite(position)
            && position >= 0 && position >= _completionSeconds)
        {
            _completionQueued = true;
            QueueLocked(true);
        }
    }

    private void QueueLocked(bool completed)
    {
        var previous = _tail;
        _tail = Task.Run(() => RunAsync(previous, completed));
    }

    private async Task RunAsync(Task previous, bool completed)
    {
        try
        {
            await previous.ConfigureAwait(false);
            if (_cancellation.IsCancellationRequested || !_isAllowed()) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_cancellation);
            deadline.CancelAfter(_syncTimeout);
            await _sync(completed, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!_cancellation.IsCancellationRequested) ReportFailure("观看记录同步超时，请检查登录状态");
        }
        catch (Exception) { ReportFailure("观看记录同步失败，请检查登录状态和网络连接"); }
    }

    private async Task FinishCoreAsync(Task pending)
    {
        await Task.Yield();
        try { await pending.WaitAsync(Timeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            _lifetime.CancelAfter(TimeSpan.Zero);
            ReportFailure("观看记录同步等待超时");
        }
        finally
        {
            if (pending.IsCompleted) _lifetime.Dispose();
            else _ = DisposeAfterAsync(pending);
        }
    }

    private async Task DisposeAfterAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        finally { _lifetime.Dispose(); }
    }

    private void ReportFailure(string message)
    {
        try { _failed?.Invoke(message); }
        catch (Exception) { /* A notice must never interrupt playback. */ }
    }
}
