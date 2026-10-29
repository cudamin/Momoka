using System.Diagnostics;
using System.Reflection;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>Real full handle, native gate and event-loop source; only the ABI endpoints are managed.</summary>
internal static class NativeResourcePerformanceTests
{
    internal static void Register(Action<string, Func<Task>> test)
    {
        test("PERF-02：三秒退出上限后，迟到线程交接 native/config/SVP 恰好回收一次", LateReaderReleasesOnceAsync);
        test("PERF-02：正常退出线程在 Destroy 前终止，不额外留回收任务", PromptReaderReleasesOnceAsync);
        test("PERF-08：native gate 挂起时 128 个 speed 只占一个待办/完成源，最终值只写一次", SpeedBurstIsBoundedAsync);
        test("PERF-08：native 写入中新增 speed 保留最新值，不丢最终唤醒", SpeedDuringWriteIsNotLostAsync);
        test("PERF-08：取消一个 superseded 等待者不取消新等待者的 speed", SpeedCancellationIsIndependentAsync);
        test("PERF-08：取消最后待办不触碰 native，后续新值可重新执行", LatestCancellationSkipsWriteAsync);
        test("PERF-08：HandOver 立即拒绝旧代 speed，Swap 后新代仍可执行", HandoverRejectsOldSpeedAsync);
        test("PERF-08：入口捕获后被换片的旧 speed 不覆盖新代的最新待办", OldAdmissionCannotReplaceNewSpeedAsync);
        test("PERF-08：Stop/Dispose 拒绝未执行 speed，relative 命令保持原逐条契约", StopRejectsSpeedAndCommandsStayDistinctAsync);
        test("PERF-08：native 写失败不遗留 running，后续 speed 可重试", FailedWorkerCanRestartAsync);
    }

    private static async Task LateReaderReleasesOnceAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Momoka-late-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "anonymous.conf");
        File.WriteAllText(source, "volume=17\n");
        var config = MpvCustomConfig.Prepare(source);
        var copy = config.FilePath;
        var lease = new CountingLease();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var f = new HandleFixture(lease: lease, config: config);
        f.Native.WaitEvent = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(12))) throw new AssertionException("测试没有放开合成 reader");
            return IntPtr.Zero;
        };
        var reader = f.StartManagedEventLoop();
        f.Native.OnDestroy = () => Assert.False(reader.IsAlive, "线程真实返回后才允许 Destroy，不只看 finally 信号");
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var watch = Stopwatch.StartNew();
            var dispose = f.Handle.DisposeAsync().AsTask();
            await dispose.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(2.7), "确实覆盖生产三秒 Join 超时");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6));
            Assert.True(reader.IsAlive);
            Assert.Equal(0, f.Native.DestroyCalls);
            Assert.Equal(0, lease.Calls);
            Assert.True(File.Exists(copy));
            Assert.True(ReferenceEquals(dispose, f.Handle.DisposeAsync().AsTask()), "重复 Dispose 不增加另一收尾者");
            var late = f.Field<Task>("_lateDestroyTask");
            Assert.False(late.IsCompleted);
            release.Set();
            await late.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(reader.IsAlive);
            Assert.Equal(1, f.Native.DestroyCalls);
            Assert.Equal(1, lease.Calls);
            Assert.False(File.Exists(copy));
            await f.Handle.DisposeAsync();
            Assert.Equal(1, f.Native.DestroyCalls);
            Assert.Equal(1, lease.Calls);
            Console.WriteLine($"    超时返回 {watch.Elapsed.TotalMilliseconds:F0} ms；线程恢复后 native/config/SVP 释放各一次");
        }
        finally
        {
            release.Set();
            Assert.True(reader.Join(TimeSpan.FromSeconds(3)), "必须先释放匿名测试线程，再释放夹具端点");
            if (f.Field<Task?>("_lateDestroyTask") is { } late) await late.WaitAsync(TimeSpan.FromSeconds(3));
            config.Dispose();
            File.Delete(source);
            Directory.Delete(folder);
        }
    }

    private static async Task PromptReaderReleasesOnceAsync()
    {
        var lease = new CountingLease();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var f = new HandleFixture(lease: lease);
        f.Native.WaitEvent = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new AssertionException("正常 reader 未放开");
            return IntPtr.Zero;
        };
        var reader = f.StartManagedEventLoop();
        f.Native.OnCommand = command =>
        {
            if (command[0] == "quit")
            {
                f.Dispatch(LibMpvNative.EventShutdown);
                release.Set();
            }
        };
        f.Native.OnDestroy = () => Assert.False(reader.IsAlive);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            await f.Handle.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, f.Native.DestroyCalls);
            Assert.Equal(1, lease.Calls);
            Assert.False(reader.IsAlive);
            Assert.True(f.Field<Task?>("_lateDestroyTask") is null);
            await f.Handle.DisposeAsync();
            Assert.Equal(1, f.Native.DestroyCalls);
        }
        finally
        {
            release.Set();
            Assert.True(reader.Join(TimeSpan.FromSeconds(3)));
        }
    }

    private static async Task SpeedBurstIsBoundedAsync()
    {
        await using var f = new HandleFixture();
        await using var hold = await GateHold.CreateAsync(f.Field<LibMpvLifetimeGate>("_apiGate"));
        var writes = Enumerable.Range(1, 128).Select(i => f.Handle.SetPropertyAsync("speed", i / 100d, CancellationToken.None)).ToArray();
        Assert.True(writes.All(task => ReferenceEquals(task, writes[0])), "整次拖动共享一个待办完成源，不创建每值 worker");
        Assert.True(f.Field<(long Generation, string Text, CancellationToken Token)?>("_pendingSpeedWrite") is { Text: "1.28" });
        Assert.Equal(0, f.Native.WrittenValues.Count);
        hold.Release();
        await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, f.Native.WrittenValues.Count);
        Assert.Equal(("speed", "1.28"), f.Native.WrittenValues.Single());
        Assert.True(f.Field<TaskCompletionSource?>("_speedWriteCompletion") is null);
        Assert.True(f.Field<(long Generation, string Text, CancellationToken Token)?>("_pendingSpeedWrite") is null);
    }

    private static async Task SpeedDuringWriteIsNotLostAsync()
    {
        await using var f = new HandleFixture();
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.Native.OnWrite = (_, _) =>
        {
            if (f.Native.WrittenValues.Count != 1) return;
            began.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new AssertionException("写入测试端点未释放");
        };
        try
        {
            var first = f.Handle.SetPropertyAsync("speed", 1.1, CancellationToken.None);
            await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var pending = Enumerable.Range(12, 117).Select(i => f.Handle.SetPropertyAsync("speed", i / 10d, CancellationToken.None)).ToArray();
            Assert.True(pending.All(task => ReferenceEquals(task, first)));
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(2, f.Native.WrittenValues.Count);
            Assert.Equal(("speed", "12.8"), f.Native.WrittenValues.Last());
        }
        finally { release.Set(); }
    }

    private static async Task SpeedCancellationIsIndependentAsync()
    {
        await using var f = new HandleFixture();
        await using var hold = await GateHold.CreateAsync(f.Field<LibMpvLifetimeGate>("_apiGate"));
        using var canceled = new CancellationTokenSource();
        var first = f.Handle.SetPropertyAsync("speed", 1.25, canceled.Token);
        var latest = f.Handle.SetPropertyAsync("speed", 1.75, CancellationToken.None);
        canceled.Cancel();
        await CanceledAsync(first);
        Assert.False(latest.IsCompleted);
        hold.Release();
        await latest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(("speed", "1.75"), f.Native.WrittenValues.Single());
    }

    private static async Task LatestCancellationSkipsWriteAsync()
    {
        await using var f = new HandleFixture();
        await using var hold = await GateHold.CreateAsync(f.Field<LibMpvLifetimeGate>("_apiGate"));
        using var canceled = new CancellationTokenSource();
        var skipped = f.Handle.SetPropertyAsync("speed", 2.5, canceled.Token);
        var drain = f.Field<TaskCompletionSource>("_speedWriteCompletion").Task;
        canceled.Cancel();
        await CanceledAsync(skipped);
        hold.Release();
        await drain.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, f.Native.WrittenValues.Count);
        await f.Handle.SetPropertyAsync("speed", 1.2, CancellationToken.None);
        Assert.Equal(("speed", "1.2"), f.Native.WrittenValues.Single());
    }

    private static async Task HandoverRejectsOldSpeedAsync()
    {
        await using var f = new HandleFixture(standalone: true);
        f.Ready();
        await using var hold = await GateHold.CreateAsync(f.Field<LibMpvLifetimeGate>("_apiGate"));
        var old = f.Handle.SetPropertyAsync("speed", 2.3, CancellationToken.None);
        f.Handle.HandOver();
        Assert.True(f.Handle.SetPropertyAsync("speed", 3, CancellationToken.None).IsCompletedSuccessfully);
        hold.Release();
        await old.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, f.Native.WrittenValues.Count);
        Assert.True(await f.Handle.SwapToAsync(HandleFixture.Request(), CancellationToken.None));
        f.Dispatch(LibMpvNative.EventStartFile);
        f.Dispatch(LibMpvNative.EventFileLoaded);
        f.Native.WrittenValues.Clear();
        await f.Handle.SetPropertyAsync("speed", 1.6, CancellationToken.None);
        Assert.Equal(("speed", "1.6"), f.Native.WrittenValues.Single());
    }

    private static async Task OldAdmissionCannotReplaceNewSpeedAsync()
    {
        await using var f = new HandleFixture(standalone: true);
        f.Ready();
        var captured = f.Handle.FileGeneration;
        f.Handle.HandOver();
        Assert.True(await f.Handle.SwapToAsync(HandleFixture.Request(), CancellationToken.None));
        f.Dispatch(LibMpvNative.EventStartFile);
        f.Dispatch(LibMpvNative.EventFileLoaded);
        f.Native.WrittenValues.Clear();
        await using var hold = await GateHold.CreateAsync(f.Field<LibMpvLifetimeGate>("_apiGate"));
        var latest = f.Handle.SetPropertyAsync("speed", 1.8, CancellationToken.None);
        // Suspend the public setter immediately after its captured generation, then run that exact
        // next production method only after the successor has already installed its pending value.
        var admission = typeof(LibMpvHandle).GetMethod("QueueSpeedWriteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stale = (Task)admission.Invoke(f.Handle, [captured, "2.7", CancellationToken.None])!;
        Assert.True(stale.IsCompletedSuccessfully, "旧代必须在入队时拒绝，不能替代新代值再等执行点跳过");
        Assert.True(f.Field<(long Generation, string Text, CancellationToken Token)?>("_pendingSpeedWrite") is { Text: "1.8" });
        hold.Release();
        await latest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(("speed", "1.8"), f.Native.WrittenValues.Single());
    }

    private static async Task StopRejectsSpeedAndCommandsStayDistinctAsync()
    {
        await using var f = new HandleFixture();
        await using var hold = await GateHold.CreateAsync(f.Field<LibMpvLifetimeGate>("_apiGate"));
        var old = f.Handle.SetPropertyAsync("speed", 1.4, CancellationToken.None);
        var stop = f.Handle.StopAsync();
        Assert.True(f.Handle.SetPropertyAsync("speed", 1.8, CancellationToken.None).IsCompletedSuccessfully);
        hold.Release();
        await Task.WhenAll(old, stop).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, f.Native.WrittenValues.Count);
        await f.Handle.DisposeAsync();
        await f.Handle.SetPropertyAsync("speed", 2, CancellationToken.None);
        Assert.Equal(0, f.Native.WrittenValues.Count);

        await using var other = new HandleFixture();
        await other.Handle.CommandAsync(["add", "volume", "1"], CancellationToken.None);
        await other.Handle.CommandAsync(["add", "volume", "2"], CancellationToken.None);
        Assert.Equal(2, other.Native.Commands.Count);
        Assert.Equal("1", other.Native.Commands[0][2]);
        Assert.Equal("2", other.Native.Commands[1][2]);
    }

    private static async Task FailedWorkerCanRestartAsync()
    {
        await using var f = new HandleFixture();
        f.Native.OnWrite = (_, _) => throw new IOException("anonymous native boundary failure");
        try
        {
            await f.Handle.SetPropertyAsync("speed", 1.3, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            throw new AssertionException("应传递非 native 返回码的测试异常");
        }
        catch (IOException) { }
        Assert.True(f.Field<TaskCompletionSource?>("_speedWriteCompletion") is null);
        f.Native.OnWrite = null;
        await f.Handle.SetPropertyAsync("speed", 1.7, CancellationToken.None);
        Assert.Equal(("speed", "1.7"), f.Native.WrittenValues.Last());
    }

    private static async Task CanceledAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); throw new AssertionException("等待者应取消"); }
        catch (OperationCanceledException) { }
    }

    private sealed class CountingLease : IDisposable
    {
        internal int Calls { get; private set; }
        public void Dispose() => Calls++;
    }

    private sealed class GateHold : IAsyncDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Task _worker;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GateHold(LibMpvLifetimeGate gate)
        {
            _worker = Task.Run(() => gate.Run(() =>
            {
                _entered.TrySetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new AssertionException("测试未释放 held native gate");
                return true;
            }));
        }
        internal static async Task<GateHold> CreateAsync(LibMpvLifetimeGate gate)
        {
            var hold = new GateHold(gate);
            try
            {
                await hold._entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                return hold;
            }
            catch
            {
                await hold.DisposeAsync();
                throw;
            }
        }
        internal void Release() => _release.Set();
        public async ValueTask DisposeAsync()
        {
            _release.Set();
            await _worker;
            _release.Dispose();
        }
    }
}
