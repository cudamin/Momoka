using System.Collections.Concurrent;
using System.Diagnostics;

namespace Momoka.Tests;

// One controlled UI continuation queue; no window, dispatcher/runtime or copied page algorithm.
internal sealed class FixtureUiQueue : SynchronizationContext, IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Call, object? State)> _queue = new();
    private readonly AutoResetEvent _posted = new(false);
    private readonly object _gate = new();
    private bool _disposed;
    internal int Pending => _queue.Count;

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _queue.Enqueue((d, state));
            _posted.Set();
        }
    }

    internal void Drain()
    {
        while (_queue.TryDequeue(out var work)) work.Call(work.State);
    }

    internal void Until(Func<bool> condition, string message, bool pump = true, int seconds = 5)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(seconds)) throw new AssertionException(message);
            if (pump) Drain();
            if (!condition()) _posted.WaitOne(10);
        }
    }

    internal void Finish(Task task)
    {
        Until(() => task.IsCompleted, "真实异步 page 方法没有完成", seconds: 17);
        if (task.Exception is { } error)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
        if (task.IsCanceled) throw new TaskCanceledException(task);
    }

    internal static void Run(Action<FixtureUiQueue> test)
    {
        var previous = Current;
        using var queue = new FixtureUiQueue();
        SetSynchronizationContext(queue);
        try { test(queue); queue.Drain(); }
        finally { SetSynchronizationContext(previous); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _posted.Dispose();
        }
    }
}
