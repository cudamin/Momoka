using System.Collections.Concurrent;

namespace Momoka.Tests;

/// <summary>One managed message-loop thread, not a Windows/XAML window or desktop dispatcher.</summary>
internal sealed class TestUiContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    internal int ThreadId => _thread.ManagedThreadId;

    internal TestUiContext()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var action in _queue.GetConsumingEnumerable()) action();
        }) { IsBackground = true, Name = "perf-images-managed-ui-fixture" };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add(() => callback(state));

    internal Task Run(Func<Task> work)
    {
        var completion = Program.Pending<bool>();
        Post(async _ =>
        {
            try { await work(); completion.TrySetResult(true); }
            catch (Exception error) { completion.TrySetException(error); }
        }, null);
        return completion.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_thread.Join(Program.Deadline)) throw new AssertionException("测试消息循环未退出");
        _queue.Dispose();
    }
}
