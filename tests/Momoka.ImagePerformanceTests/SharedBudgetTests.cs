using System.Collections.Concurrent;
using System.Diagnostics;
using Momoka.Infrastructure;
using static Momoka.Tests.Program;

namespace Momoka.Tests;

internal static class SharedBudgetTests
{
    internal static void Register()
    {
        Test("共享预算：500不同键的队列/执行/需求峰值有界，离开后归零", async () =>
        {
            var work = new SharedWork<int>(3, 7, 12);
            var gate = Pending<int>();
            var callers = new List<CancellationTokenSource>();
            var waits = new List<Task<int>>();
            var started = 0;
            var peakRunning = 0;
            var peakQueued = 0;
            var peakDemands = 0;
            for (var index = 0; index < 500; index++)
            {
                var source = new CancellationTokenSource();
                callers.Add(source);
                waits.Add(work.RunAsync($"key-{index}", async token =>
                {
                    Interlocked.Increment(ref started);
                    return await gate.Task.WaitAsync(token).ConfigureAwait(false);
                }, source.Token));
                peakRunning = Math.Max(peakRunning, work.Running);
                peakQueued = Math.Max(peakQueued, work.Queued);
                peakDemands = Math.Max(peakDemands, work.Demands);
                Assert.True(work.Executing <= 3 && work.Queued <= 7 && work.Running <= 10 && work.Demands <= 12);
            }
            foreach (var source in callers) source.Cancel();
            Assert.Equal(0, work.Demands, "取消当场释放需求，不等500个异步finally");
            Assert.Equal(0, work.Queued);
            foreach (var wait in waits) await Cancelled(wait);
            await Eventually(() => work.Running == 0, "取消的执行槽应实际退出");
            Assert.True(started <= 3, "不是500工厂挤在Semaphore后面");
            Measurements.Add(new { kind = "shared-500-distinct", keys = 500, peakRunning, peakQueued, peakDemands, factoriesStarted = started, remaining = work.Running });
            foreach (var source in callers) source.Dispose();
        });

        Test("共享预算：首等者取消不杀另一有效需求", async () =>
        {
            var work = new SharedWork<int>(1, 2, 8);
            var started = Pending<CancellationToken>();
            var result = Pending<int>();
            var calls = 0;
            using var firstSource = new CancellationTokenSource();
            var first = work.RunAsync("same", token =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult(token);
                return result.Task;
            }, firstSource.Token);
            var second = work.RunAsync("same", _ => throw new AssertionException("不能开第二下载"), CancellationToken.None);
            var sharedToken = await started.Task.WaitAsync(Deadline);
            firstSource.Cancel();
            await Cancelled(first);
            Assert.False(sharedToken.IsCancellationRequested);
            Assert.Equal(1, work.Demands);
            result.SetResult(42);
            Assert.Equal(42, await second.WaitAsync(Deadline));
            await Eventually(() => work.Running == 0, "完成必须退场");
            Assert.Equal(1, calls);
        });

        Test("共享预算：需求归零撤销在途，旧键结束不淘汰新同键", async () =>
        {
            var work = new SharedWork<int>(1, 3, 8);
            var oldStarted = Pending<CancellationToken>();
            var oldResult = Pending<int>();
            var newStarted = Pending<bool>();
            var newResult = Pending<int>();
            using var source = new CancellationTokenSource();
            var old = work.RunAsync("same", token => { oldStarted.SetResult(token); return oldResult.Task; }, source.Token);
            var oldToken = await oldStarted.Task.WaitAsync(Deadline);
            source.Cancel();
            await Cancelled(old);
            Assert.True(oldToken.IsCancellationRequested);
            var fresh = work.RunAsync("same", _ => { newStarted.SetResult(true); return newResult.Task; }, CancellationToken.None);
            Assert.Equal(1, work.Executing, "不合作的旧工厂未退出，不可提早还槽");
            Assert.Equal(1, work.Queued);
            Assert.False(newStarted.Task.IsCompleted);
            oldResult.SetResult(1);
            await newStarted.Task.WaitAsync(Deadline);
            var joined = work.RunAsync("same", _ => throw new AssertionException("旧回调删了新键"), CancellationToken.None);
            Assert.Equal(2, work.Demands);
            newResult.SetResult(7);
            Assert.Equal(7, await fresh.WaitAsync(Deadline));
            Assert.Equal(7, await joined.WaitAsync(Deadline));
            await Eventually(() => work.Running == 0, "旧同键/新同键完成都清理");
        });

        Test("共享预算：未启动离屏需求取消，当前可见不被旧排队抢占", async () =>
        {
            var work = new SharedWork<int>(1, 3, 8);
            var blocker = Pending<int>();
            var order = new ConcurrentQueue<string>();
            var active = work.RunAsync("active", _ => blocker.Task, CancellationToken.None);
            using var goneSource = new CancellationTokenSource();
            var gone = work.RunAsync("gone", _ => { order.Enqueue("gone"); return Task.FromResult(1); }, goneSource.Token);
            goneSource.Cancel();
            await Cancelled(gone);
            Assert.Equal(0, work.Queued);
            var visible = work.RunAsync("visible", _ => { order.Enqueue("visible"); return Task.FromResult(2); }, CancellationToken.None);
            blocker.SetResult(0);
            await active.WaitAsync(Deadline);
            Assert.Equal(2, await visible.WaitAsync(Deadline));
            Assert.Equal("visible", string.Join(',', order));
        });

        Test("共享预算：新的及续等可见需求优先旧未启动键", async () =>
        {
            var work = new SharedWork<int>(1, 4, 8);
            var blocker = Pending<int>();
            var order = new ConcurrentQueue<string>();
            var active = work.RunAsync("active", _ => blocker.Task, CancellationToken.None);
            var b = work.RunAsync("b", _ => { order.Enqueue("b"); return Task.FromResult(2); }, CancellationToken.None);
            var c = work.RunAsync("c", _ => { order.Enqueue("c"); return Task.FromResult(3); }, CancellationToken.None);
            var bRenewed = work.RunAsync("b", _ => throw new AssertionException("相同键仍应共享"), CancellationToken.None);
            blocker.SetResult(0);
            await Task.WhenAll(active, b, c, bRenewed).WaitAsync(Deadline);
            Assert.Equal("b,c", string.Join(',', order));
        });

        Test("共享预算：队列溢出撤销最旧整键，活动共享下载不受伤", async () =>
        {
            var work = new SharedWork<int>(1, 1, 5);
            var blocker = Pending<int>();
            var activeStarted = Pending<CancellationToken>();
            var active = work.RunAsync("active", token => { activeStarted.SetResult(token); return blocker.Task; }, CancellationToken.None);
            var old = work.RunAsync("old", _ => throw new AssertionException("淘汰工厂不能执行"), CancellationToken.None);
            var oldJoin = work.RunAsync("old", _ => throw new AssertionException("旧共享工厂不能执行"), CancellationToken.None);
            var newer = work.RunAsync("new", _ => Task.FromResult(9), CancellationToken.None);
            await Cancelled(old);
            await Cancelled(oldJoin);
            Assert.False((await activeStarted.Task.WaitAsync(Deadline)).IsCancellationRequested);
            Assert.Equal(2, work.Demands);
            blocker.SetResult(1);
            Assert.Equal(1, await active.WaitAsync(Deadline));
            Assert.Equal(9, await newer.WaitAsync(Deadline));
        });

        Test("共享预算：500同键等待者也有预算，超额即撤销不藏排队", async () =>
        {
            var work = new SharedWork<int>(1, 1, 16);
            var result = Pending<int>();
            var waits = Enumerable.Range(0, 500).Select(_ => work.RunAsync("hot", _ => result.Task, CancellationToken.None)).ToArray();
            Assert.Equal(16, work.Demands);
            Assert.Equal(1, work.Running);
            Assert.Equal(484, waits.Count(task => task.IsCanceled));
            result.SetResult(5);
            for (var i = 0; i < 16; i++) Assert.Equal(5, await waits[i].WaitAsync(Deadline));
            for (var i = 16; i < 500; i++) await Cancelled(waits[i]);
            await Eventually(() => work.Running == 0 && work.Demands == 0, "热门键也必须清理");
        });

        Test("共享预算：同步/异步故障移除，下一次可重新执行", async () =>
        {
            var work = new SharedWork<int>(1, 2, 4);
            var sync = work.RunAsync("retry", _ => throw new InvalidOperationException("sync"), CancellationToken.None);
            Assert.True(await Failure(sync) is InvalidOperationException);
            await Eventually(() => work.Running == 0 && work.Demands == 0, "同步失败清理");
            var asyncFailure = work.RunAsync("retry", _ => Task.FromException<int>(new IOException("async")), CancellationToken.None);
            Assert.True(await Failure(asyncFailure) is IOException);
            await Eventually(() => work.Running == 0 && work.Demands == 0, "异步失败清理");
            Assert.Equal(8, await work.RunAsync("retry", _ => Task.FromResult(8), CancellationToken.None).WaitAsync(Deadline));
        });

        Test("共享预算：预取消不启动，构造参数拒绝零预算", async () =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SharedWork<int>(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SharedWork<int>(1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SharedWork<int>(3, 1, 2));
            var work = new SharedWork<int>(1, 1, 1);
            using var source = new CancellationTokenSource();
            source.Cancel();
            await Cancelled(work.RunAsync("none", _ => throw new AssertionException("预取消不能跑"), source.Token));
            Assert.Equal(0, work.Running);
            Assert.Equal(0, work.Demands);
        });

        Test("共享预算：取消回调不阻塞调用线程，仍占执行槽直到实际退出", async () =>
        {
            var work = new SharedWork<int>(1, 2, 4);
            var started = Pending<bool>();
            var callbackStarted = Pending<bool>();
            using var callbackRelease = new ManualResetEventSlim(false);
            using var source = new CancellationTokenSource();
            var old = work.RunAsync("old", async token =>
            {
                token.Register(() => { callbackStarted.TrySetResult(true); callbackRelease.Wait(Deadline); });
                started.SetResult(true);
                while (!token.IsCancellationRequested) await Task.Delay(1).ConfigureAwait(false);
                return 0;
            }, source.Token);
            await started.Task.WaitAsync(Deadline);
            var clock = Stopwatch.StartNew();
            source.Cancel();
            Assert.True(clock.ElapsedMilliseconds < 1000, "不能在取消方同步跑长HTTP取消回调");
            await Cancelled(old);
            await callbackStarted.Task.WaitAsync(Deadline);
            var fresh = work.RunAsync("fresh", _ => Task.FromResult(4), CancellationToken.None);
            Assert.Equal(1, work.Executing);
            Assert.Equal(1, work.Queued);
            Assert.False(fresh.IsCompleted);
            callbackRelease.Set();
            Assert.Equal(4, await fresh.WaitAsync(Deadline));
            await Eventually(() => work.Running == 0, "回调完成后必须释放槽");
        });

        Test("共享预算：取消回调故障不外溢、不占死预算", async () =>
        {
            var work = new SharedWork<int>(1, 2, 4);
            var started = Pending<bool>();
            using var source = new CancellationTokenSource();
            var old = work.RunAsync("old", async token =>
            {
                token.Register(() => throw new IOException("callback"));
                started.SetResult(true);
                while (!token.IsCancellationRequested) await Task.Delay(1).ConfigureAwait(false);
                return 0;
            }, source.Token);
            await started.Task.WaitAsync(Deadline);
            source.Cancel();
            await Cancelled(old);
            await Eventually(() => work.Running == 0, "故障回调清理");
            Assert.Equal(2, await work.RunAsync("new", _ => Task.FromResult(2), CancellationToken.None).WaitAsync(Deadline));
        });

        Test("共享预算：并发加入/取消/完成竞争计数不负、不超额", async () =>
        {
            var work = new SharedWork<int>(3, 9, 30);
            var gate = Pending<int>();
            var sources = Enumerable.Range(0, 120).Select(_ => new CancellationTokenSource()).ToArray();
            var waits = new ConcurrentBag<Task<int>>();
            Parallel.For(0, sources.Length, i => waits.Add(
                work.RunAsync($"race-{i % 12}", token => gate.Task.WaitAsync(token), sources[i].Token)));
            Parallel.ForEach(sources, source => source.Cancel());
            gate.TrySetResult(5);
            foreach (var wait in waits) await Failure(wait);
            await Eventually(() => work.Running == 0 && work.Demands == 0, "竞争后清理");
            Assert.Equal(0, work.Executing);
            Assert.Equal(0, work.Queued);
            foreach (var source in sources) source.Dispose();
        });
    }
}
