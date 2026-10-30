using System.Reflection;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DanmakuClientLifetimeTests
{
    internal static void Register()
    {
        Test("PERF-05关闭反证：热缓存阻塞后取消/Dispose，退出前保留请求闸门，恢复不抛Disposed", () =>
            CacheDisposalWaitsAsync().GetAwaiter().GetResult());
        Test("PERF-05关闭反证：原样宿主旧加载与被替换加载都排空，取消不逸出异步UI方法", () =>
            SupersededLoadsDrainAsync().GetAwaiter().GetResult());
        Test("PERF-05关闭反证：排队搜索/发送一起取消，新入场拒绝且不增加HTTP", () =>
            AllQueuedWorkDrainsAsync().GetAwaiter().GetResult());
    }

    private static async Task CacheDisposalWaitsAsync()
    {
        using var f = new DanmakuFixture(DanmakuFixture.Body(40000, 300));
        await f.WarmAsync();
        f.Transport.Forbid = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        f.CacheReadHook = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(6))) throw new AssertionException("未放开缓存文件读取边界");
        };
        var reading = f.Client.GetCommentsAsync(7, true, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            f.Client.Dispose();
            var shutdown = f.Client.ShutdownAsync();
            Assert.False(shutdown.IsCompleted, "关闭不能释放仍由后台finally拥有的HTTP/semaphore");
            Assert.True(ReferenceEquals(shutdown, f.Client.ShutdownAsync()));
            var gate = (SemaphoreSlim)typeof(DandanPlayClient).GetField("_requests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Client)!;
            Assert.Equal(0, gate.CurrentCount, "在途读取仍持有未释放的实际请求闸门");
            release.Set();
            await CancelledAsync(reading);
            await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, f.Transport.Requests, "热缓存关闭没有额外HTTP");
            await CancelledAsync(f.Client.GetCommentsAsync(7, true));
        }
        finally
        {
            release.Set();
            try { await reading; } catch (OperationCanceledException) { }
            await f.Client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task SupersededLoadsDrainAsync()
    {
        using var f = new DanmakuFixture(DanmakuFixture.Body(50));
        await f.WarmAsync();
        f.Transport.Forbid = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.CacheReadHook = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(6))) throw new AssertionException("旧宿主缓存读边界未放开");
        };
        using var model = new PlayerViewModel(f.Client, f.Settings);
        var old = model.LoadDanmakuAsync(new DandanPlayEpisode(7, "fixture", "old"));
        Task? next = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            next = model.LoadDanmakuAsync(new DandanPlayEpisode(7, "fixture", "next"));
            model.Dispose();
            f.Client.Dispose();
            var shutdown = f.Client.ShutdownAsync();
            Assert.False(shutdown.IsCompleted, "新任务取消不能让旧读取提前释放gate");
            release.Set();
            await Task.WhenAll(old, next).WaitAsync(TimeSpan.FromSeconds(3));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, model.Pushes);
            Assert.Equal(0, model.Applied.Count);
            Assert.Equal(1, f.Transport.Requests);
        }
        finally
        {
            release.Set();
            await old.WaitAsync(TimeSpan.FromSeconds(3));
            if (next is not null) await next.WaitAsync(TimeSpan.FromSeconds(3));
            await f.Client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task AllQueuedWorkDrainsAsync()
    {
        using var f = new DanmakuFixture(DanmakuFixture.Body(10));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Credentials.BeforeReturn = async _ => { entered.TrySetResult(); await release.Task; };
        var read = f.Client.GetCommentsAsync(7, true);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var search = f.Client.SearchAsync("fixture", null);
            var send = f.Client.SendCommentAsync(7, 1, "fixture", CancellationToken.None);
            var shutdown = f.Client.ShutdownAsync();
            Assert.False(shutdown.IsCompleted);
            await CancelledAsync(search);
            await CancelledAsync(send);
            release.TrySetResult();
            await CancelledAsync(read);
            await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(0, f.Transport.Requests, "关停前queued/read未派发HTTP");
            await CancelledAsync(f.Client.SearchAsync("fixture", null));
            await CancelledAsync(f.Client.SendCommentAsync(7, 1, "fixture"));
            Assert.Equal(0, f.Transport.Requests);
        }
        finally
        {
            release.TrySetResult();
            try { await read; } catch (OperationCanceledException) { }
            await f.Client.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task CancelledAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); throw new AssertionException("应保留正常取消"); }
        catch (OperationCanceledException) { }
    }
}
