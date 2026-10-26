using System.Collections.Concurrent;
using System.Diagnostics;

namespace Momoka.Tests;

// A single-thread queue for the real async-void handlers. No sleeps, WinUI/native or task blocking.
internal sealed class UiQueue : SynchronizationContext, IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _work = new();
    private readonly AutoResetEvent _ready = new(false);
    private TaskCompletionSource? _idle;
    private int _operations;

    public override void Post(SendOrPostCallback callback, object? state)
    {
        _work.Enqueue((callback, state));
        _ready.Set();
    }

    public override void OperationStarted() => _operations++;
    public override void OperationCompleted()
    {
        if (--_operations == 0)
        {
            var idle = _idle;
            _idle = null;
            idle?.TrySetResult();
        }
    }

    internal Task IdleAsync() => _operations == 0 ? Task.CompletedTask : (_idle ??= new()).Task;

    internal void Run(Func<Task> scenario)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            var task = scenario();
            var deadline = Stopwatch.StartNew();
            while (!task.IsCompleted || _operations > 0 || !_work.IsEmpty)
            {
                if (_work.TryDequeue(out var work)) work.Callback(work.State);
                else Assert.True(_ready.WaitOne(TimeSpan.FromSeconds(10)), "Source fixture made no progress");
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(20), "Source fixture did not settle");
            }
            if (task.Exception is { } error) throw error.InnerException ?? error;
            Assert.False(task.IsCanceled, "Unexpected fixture cancellation");
        }
        finally { SetSynchronizationContext(previous); }
    }

    public void Dispose() => _ready.Dispose();
}
