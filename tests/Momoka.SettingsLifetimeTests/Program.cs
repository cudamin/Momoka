using System.Collections.Concurrent;
using System.Text;

namespace Momoka.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is not [var shellAssembly, var sourceRoot])
        {
            Console.Error.WriteLine("用法：Momoka.SettingsLifetimeTests <本树当前 Release Momoka.dll> <本树根目录>");
            return 2;
        }
        Console.WriteLine("R-018／R-026 真实设置 VM + 原样 page/window 控制流回归；假 HTTP／无 WinUI、账号与凭据操作");
        try
        {
            _ = new ShellRuntime(shellAssembly, sourceRoot);
            AdministrationLifetimeTests.Register();
            SettingsHiddenLifetimeTests.Register();
            SaveStateLifetimeTests.Register();
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

/// <summary>Keep true VM ConfigureAwait(true) and queued UI callbacks deterministic without a WinUI loop.</summary>
internal sealed class FixtureContext : SynchronizationContext, IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly AutoResetEvent _ready = new(false);
    private readonly SynchronizationContext? _previous = Current;
    public FixtureContext() => SetSynchronizationContext(this);
    public override void Post(SendOrPostCallback callback, object? state) { _queue.Enqueue((callback, state)); _ready.Set(); }
    public void Drain()
    {
        while (_queue.TryDequeue(out var callback)) callback.Callback(callback.State);
    }
    public void PumpUntil(Func<bool> predicate)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!predicate())
        {
            Drain();
            if (Environment.TickCount64 >= deadline) throw new AssertionException("定向夹具超过 10 秒仍未达到状态");
            if (!predicate()) _ready.WaitOne(1);
        }
        Drain();
    }
    public void Await(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        if (task.IsFaulted) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(task.Exception!.InnerException!).Throw();
        if (task.IsCanceled) throw new AssertionException("夹具顶层任务意外取消");
    }
    public void Dispose() { SetSynchronizationContext(_previous); _ready.Dispose(); }
}
