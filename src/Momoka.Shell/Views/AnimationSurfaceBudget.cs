namespace Momoka.Shell.Views;

/// <summary>Retains native pixel reservations until cancelled decoding actually exits or a held frame clears.</summary>
internal sealed class AnimationSurfaceBudget(long capacity)
{
    private readonly object _gate = new();
    private long _used;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task<IDisposable> ReserveAsync(long bytes, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes, capacity);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Task changed;
            lock (_gate)
            {
                if (bytes <= capacity - _used)
                {
                    _used += bytes;
                    return new Reservation(this, bytes);
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private void Release(long bytes)
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            _used -= bytes;
            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        changed.TrySetResult();
    }

    private sealed class Reservation(AnimationSurfaceBudget owner, long bytes) : IDisposable
    {
        private AnimationSurfaceBudget? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(bytes);
    }
}
