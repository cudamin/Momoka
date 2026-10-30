using System.Diagnostics;
using System.Text;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DanmakuPerformanceTests
{
    internal sealed record Sample(int TextLength, int Iteration, double CallReturnMs, double MarkerWaitMs,
        double TotalMs, bool CompletedAtReturn, bool MarkerBeforeCompletion, int UiThread,
        int CredentialThread, int CacheThread, int Count, int HttpRequests);

    internal static async Task<IReadOnlyList<Sample>> MeasureAsync()
    {
        var samples = new List<Sample>();
        using var ui = new FifoContext();
        foreach (var length in new[] { 24, 300 })
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40000, length));
            await fixture.WarmAsync().ConfigureAwait(false);
            fixture.Transport.Forbid = true;
            // 相同公开 API/缓存的两次预热，正式样本不强制 GC、不 Sleep、不注入 I/O 延迟。
            for (var warm = 0; warm < 2; warm++) await fixture.Client.GetCommentsAsync(7, true).ConfigureAwait(false);
            for (var iteration = 0; iteration < 5; iteration++)
            {
                var sample = await ui.InvokeAsync(async () =>
                {
                    var watch = Stopwatch.StartNew();
                    var marker = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var finished = false;
                    var markerBefore = false;
                    ui.Post(_ => { markerBefore = !finished; marker.SetResult(watch.Elapsed.TotalMilliseconds); }, null);
                    var pending = fixture.Client.GetCommentsAsync(7, true);
                    var callMs = watch.Elapsed.TotalMilliseconds;
                    var completed = pending.IsCompleted;
                    var result = await pending.ConfigureAwait(true);
                    finished = true;
                    var total = watch.Elapsed.TotalMilliseconds;
                    var markerMs = await marker.Task.ConfigureAwait(true);
                    return new Sample(length, iteration, callMs, markerMs, total, completed, markerBefore,
                        Environment.CurrentManagedThreadId, fixture.Credentials.LastThread, fixture.CacheThread,
                        result.Count, fixture.Transport.Requests);
                }).ConfigureAwait(false);
                samples.Add(sample);
            }
        }
        foreach (var group in samples.GroupBy(sample => sample.TextLength))
        {
            static double Median(IEnumerable<double> values) => values.Order().ElementAt(2);
            Console.WriteLine($"缓存 40000×{group.Key}：调用返回 {Median(group.Select(s => s.CallReturnMs)):F3}ms，"
                + $"FIFO 标记 {Median(group.Select(s => s.MarkerWaitMs)):F3}ms，总耗时 {Median(group.Select(s => s.TotalMs)):F3}ms；"
                + $"返回即完成 {group.Count(s => s.CompletedAtReturn)}/5，额外 HTTP {group.Max(s => s.HttpRequests) - 1}。");
        }
        return samples;
    }

    internal static void Register()
    {
        Test("PERF-05：预热四万条长缓存让出 FIFO UI，读取、凭据不在 UI，稳定排序不丢条", () =>
        {
            using var ui = new FifoContext();
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40000, 300));
            fixture.WarmAsync().GetAwaiter().GetResult();
            fixture.Transport.Forbid = true;
            var result = ui.InvokeAsync(async () =>
            {
                var marker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<IReadOnlyList<DanmakuComment>>? pending = null;
                ui.Post(_ => marker.SetResult(pending is not null && !pending.IsCompleted), null);
                pending = fixture.Client.GetCommentsAsync(7, true);
                Assert.False(pending.IsCompleted, "大缓存不能在公开调用返回前同步整理完。");
                Assert.True(await marker.Task.ConfigureAwait(true), "UI FIFO 标记等待了完整批量整理。");
                return await pending.ConfigureAwait(true);
            }).GetAwaiter().GetResult();
            Assert.Equal(40000, result.Count);
            Assert.True(result.Zip(result.Skip(1)).All(pair => pair.First.Time <= pair.Second.Time));
            Assert.True(fixture.CacheThread != ui.ThreadId, "缓存读取仍在 UI。");
            Assert.True(fixture.Credentials.LastThread != ui.ThreadId);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("PERF-05：预取消大缓存不读凭据/缓存、不联网，后续请求仍可用", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40000, 300));
            fixture.WarmAsync().GetAwaiter().GetResult();
            fixture.Transport.Forbid = true;
            var calls = fixture.Credentials.Calls;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Client.GetCommentsAsync(7, true, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(calls, fixture.Credentials.Calls);
            Assert.Equal(40000, fixture.Client.GetCommentsAsync(7, true).GetAwaiter().GetResult().Count);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("PERF-05：后台缓存阶段取消释放请求闸门，不返回取消后的成功结果", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(40000, 300));
            fixture.WarmAsync().GetAwaiter().GetResult();
            fixture.Transport.Forbid = true;
            using var reached = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var blocked = 0;
            fixture.CacheReadHook = () =>
            {
                if (Interlocked.Exchange(ref blocked, 1) != 0) return;
                reached.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("私有缓存阶段未释放。");
            };
            using var cancellation = new CancellationTokenSource();
            var pending = fixture.Client.GetCommentsAsync(7, true, cancellation.Token);
            try
            {
                Assert.True(reached.Wait(TimeSpan.FromSeconds(5)), "没有进入后台缓存阶段。");
                cancellation.Cancel();
            }
            finally { release.Set(); }
            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            fixture.CacheReadHook = null;
            Assert.Equal(40000, fixture.Client.GetCommentsAsync(7, true).GetAwaiter().GetResult().Count);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("PERF-05：请求仍串行，排队取消不发 HTTP，也不锁死后继", () =>
        {
            using var fixture = new DanmakuFixture(DanmakuFixture.Body(10));
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Transport.BeforeReturn = async token => { reached.TrySetResult(); await release.Task.WaitAsync(token).ConfigureAwait(false); };
            var first = fixture.Client.GetCommentsAsync(7, true);
            reached.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            using var cancellation = new CancellationTokenSource();
            var queued = fixture.Client.GetCommentsAsync(8, true, cancellation.Token);
            cancellation.Cancel();
            try
            {
                Assert.Throws<OperationCanceledException>(() => queued.GetAwaiter().GetResult());
                Assert.Equal(1, fixture.Transport.Requests);
            }
            finally { release.TrySetResult(); }
            Assert.Equal(10, first.GetAwaiter().GetResult().Count);
            fixture.Transport.BeforeReturn = null;
            Assert.Equal(10, fixture.Client.GetCommentsAsync(8, true).GetAwaiter().GetResult().Count);
            Assert.Equal(2, fixture.Transport.Requests);
        });
        Test("PERF-05：缓存损坏仍重取、同时间顺序稳定、非法条目丢弃", () =>
        {
            const string body = """{"comments":[{"p":"2,1,1","m":"first"},{"p":"1,5,2","m":"earlier"},{"p":"2,4,3","m":"second"},{"p":"0,7,4","m":"invalid"}]}""";
            using var fixture = new DanmakuFixture(Encoding.UTF8.GetBytes(body));
            fixture.WarmAsync().GetAwaiter().GetResult();
            File.WriteAllText(Directory.GetFiles(fixture.Cache, "*.json").Single(), "{broken");
            var result = fixture.Client.GetCommentsAsync(7, true).GetAwaiter().GetResult();
            Assert.Equal(3, result.Count);
            Assert.Equal("earlier", result[0].Text);
            Assert.Equal("first", result[1].Text);
            Assert.Equal("second", result[2].Text);
            Assert.Equal(2, fixture.Transport.Requests);
        });
    }
}
