namespace Momoka.Playback;

/// <summary>Owns an ended reusable player until a successor claims it or the caller releases it.
/// Only disposal and cancellation observation are public, never the underlying handle.</summary>
public sealed class PlaybackContinuation : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lazy<Task> _release;
    public CancellationToken Lifetime { get; }
    internal Task ContextClosed { get; }

    internal PlaybackContinuation(Func<Task> release, Task contextClosed)
    {
        ContextClosed = contextClosed;
        _release = new Lazy<Task>(release, LazyThreadSafetyMode.ExecutionAndPublication);
        Lifetime = _lifetime.Token;
    }

    // Marks cancellation synchronously without invoking arbitrary caller callbacks under the request lock.
    internal Task CancelAsync() => _lifetime.CancelAsync();
    public ValueTask DisposeAsync() => new(_release.Value);
}
