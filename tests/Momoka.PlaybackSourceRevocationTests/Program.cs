using System.Collections.Concurrent;
using System.Diagnostics;
using Momoka.Playback;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 0) throw new AssertionException("本项目只运行 R044 独立专项");
        RegisterPreparation();
        RegisterRunning();
        RegisterReplacement();
        RegisterShutdown();
        var failures = new List<(string Name, Exception Error)>();
        var stopwatch = Stopwatch.StartNew();
        foreach (var test in Cases)
        {
            try
            {
                await test.Run();
                Console.WriteLine("  通过  " + test.Name);
            }
            catch (Exception error)
            {
                failures.Add((test.Name, error));
                Console.WriteLine("  失败  " + test.Name);
            }
        }
        foreach (var failure in failures)
        {
            Console.WriteLine("── " + failure.Name);
            Console.WriteLine(failure.Error is AssertionException ? failure.Error.Message : failure.Error.ToString());
        }
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failures.Count}，失败 {failures.Count}，跳过 0，用时 {stopwatch.ElapsedMilliseconds} ms");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Case(string name, Func<Task> run) => Cases.Add(("R044 " + name, run));

    private static void RegisterPreparation()
    {
        Case("已撤销身份不借默认来源、不碰原活动后端", async () =>
        {
            await using var f = new Fixture();
            var old = f.Handle();
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(old);
            var playing = f.Play(f.B, "existing");
            await Signals.Await(old.Listening(0), "原播放开始");
            Fixture.Revoke(f.A);
            await Signals.Cancelled(f.Play(f.A, "revoked", explicitScope: false));
            Assert.Equal(1, f.Backend.Starts.Count);
            Assert.Equal(0, old.StopCount);
            Assert.Equal(0, old.DisposeCount);
            Assert.False(old.RunningTokens.Single().IsCancellationRequested);
            old.End();
            await Signals.Await(playing, "原播放仍可结束");
            f.AssertSources(f.A);
        });

        foreach (var explicitScope in new[] { false, true })
        {
            Case("后端准备收到所属 scope 撤销 " + explicitScope, async () =>
            {
                await using var f = new Fixture();
                var entered = Signals.New();
                var handle = f.Handle();
                f.Backend.Next = async (_, token) =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return handle;
                };
                var run = f.Play(f.A, "preparing", explicitScope: explicitScope);
                await Signals.Await(entered.Task, "进入后端准备");
                f.Default(f.B);
                Fixture.Revoke(f.A);
                Assert.True(f.Backend.StartupTokens.Single().IsCancellationRequested, "真实后端 startup 令牌必须撤销");
                await Signals.Cancelled(run);
                Assert.Equal(0, f.Transport.Reports.Count);
                Assert.Equal(0, handle.StopCount);
                Assert.Equal(0, handle.DisposeCount, "未返回句柄仍由后端的 startup 负责");
                f.AssertSources(f.A);
                await f.NextSucceeds();
            });
        }

        Case("准备忽略取消后迟到句柄停止释放一次且不宣布起播", async () =>
        {
            await using var f = new Fixture();
            var entered = Signals.New();
            var release = f.Hold();
            var handle = f.Handle();
            var notices = new ConcurrentQueue<string>();
            f.Service.NowPlayingChanged += item => { if (item is not null) notices.Enqueue(item.Id); };
            f.Backend.Next = async (_, _) => { entered.TrySetResult(); await release.Task; return handle; };
            var run = f.Play(f.A, "late-start");
            await Signals.Await(entered.Task, "准备挂起");
            Fixture.Revoke(f.A);
            var canceled = f.Backend.StartupTokens.Single().IsCancellationRequested;
            release.TrySetResult();
            await Signals.Cancelled(run);
            Assert.True(canceled);
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
            Assert.Equal(0, notices.Count);
            Assert.Equal(0, f.Transport.Reports.Count);
            f.AssertSources(f.A);
            await f.NextSucceeds();
        });

        Case("撤销挂起 fullscreen 准备不停止其它来源现片", async () =>
        {
            await using var f = new Fixture();
            var entered = Signals.New();
            var native = f.Handle(pictureInHostWindow: false);
            native.ReadText = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; };
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(native);
            var playing = f.Play(f.B, "existing");
            await Signals.Await(native.Listening(0), "原来源在播");
            var next = f.Play(f.A, "reading-fullscreen");
            await Signals.Await(entered.Task, "新请求读现片窗口状态");
            Fixture.Revoke(f.A);
            await Signals.Cancelled(next);
            Assert.Equal(0, native.StopCount);
            Assert.False(native.RunningTokens.Single().IsCancellationRequested);
            Assert.Equal(1, f.Backend.Starts.Count);
            native.End();
            await Signals.Await(playing, "原来源正常完成");
            Assert.Equal(1, native.DisposeCount);
        });

        Case("已建句柄但起播轨道快照挂起也须收尾", async () =>
        {
            await using var f = new Fixture();
            var entered = Signals.New();
            var handle = f.Handle();
            handle.ReadTracks = async token => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return []; };
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "initial-tracks");
            await Signals.Await(entered.Task, "真实初始轨道读取");
            Fixture.Revoke(f.A);
            var result = await Signals.Await(run, "撤销初读完成");
            Assert.Equal(PlaybackEndReason.Stopped, result.Exit.Reason);
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
            Assert.Equal(1, f.Finishes("initial-tracks"));
            Assert.Equal(0, f.Transport.Starts("initial-tracks"));
            f.AssertSources(f.A);
            await f.NextSucceeds();
        });
    }

    private static void RegisterRunning()
    {
        foreach (var explicitScope in new[] { false, true })
            foreach (var report in new[] { false, true })
                foreach (var controlled in new[] { false, true })
                {
                    Case($"已启动撤销固定身份 explicit={explicitScope}/report={report}/control={controlled}", async () =>
                    {
                        await using var f = new Fixture();
                        f.Settings.Playback.ReportProgressToServer = report;
                        var handle = f.Handle(controlled: controlled);
                        f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                        var run = f.Play(f.A, "running", explicitScope: explicitScope);
                        await Signals.Await(handle.Listening(0), "真实 monitor 已建立");
                        f.Default(f.B);
                        Assert.False(handle.RunningTokens.Single().IsCancellationRequested, "默认切 source 不撤销旧 scope");
                        Assert.Equal(0, handle.StopCount);
                        Fixture.Revoke(f.A);
                        Assert.True(handle.RunningTokens.Single().IsCancellationRequested, "真正 running 令牌必须接到撤销");
                        var result = await Signals.Await(run, "所属身份撤销结束播放");
                        Assert.Equal(PlaybackEndReason.Stopped, result.Exit.Reason);
                        Assert.False(result.ProgressReported, "已签出身份上报可取消，不能转投默认来源");
                        Assert.False(result.MarkedWatched);
                        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, result.PositionTicks);
                        Assert.Equal(1, handle.StopCount);
                        Assert.Equal(1, handle.DisposeCount);
                        Assert.Equal(1, f.Finishes("running"));
                        Assert.False(f.Service.IsPlaying);
                        Assert.Equal(0, f.Transport.Stops("running"));
                        Assert.True(f.Transport.Reports.All(sent => sent.Host == "a.invalid"));
                        f.AssertSources(f.A);
                        await f.NextSucceeds();
                    });
                }

        Case("退出等待忽略取消仍由 monitor timer 收尾后端", async () =>
        {
            await using var f = new Fixture();
            var handle = f.Handle(honorWaitCancellation: false);
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "ignoring-exit");
            await Signals.Await(handle.Listening(0), "进入忽略取消的 wait");
            Fixture.Revoke(f.A);
            await Signals.Await(run, "即使 exit 假实现忽略取消也能结束");
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
            Assert.Equal(1, f.Finishes("ignoring-exit"));
        });

        Case("默认来源切换及来源通知均不撤销 still-available 播放", async () =>
        {
            await using var f = new Fixture();
            var handle = f.Handle();
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "cross-source");
            await Signals.Await(handle.Listening(0), "原来源正在播");
            f.Default(f.B);
            f.Default(f.C);
            Fixture.Revoke(f.B);
            f.NotifySourcesChanged();
            Assert.True(f.A.IsAvailable);
            Assert.False(handle.RunningTokens.Single().IsCancellationRequested);
            Assert.False(f.Backend.StartupTokens.Single().IsCancellationRequested);
            Assert.Equal(0, handle.StopCount);
            handle.End(PlaybackEndReason.UserQuit, 12);
            var result = await Signals.Await(run, "默认变更后正常退出");
            Assert.True(result.ProgressReported);
            Assert.Equal(1, f.Transport.Stops("cross-source"));
            Assert.True(f.Transport.Reports.All(sent => sent.Host == "a.invalid"));
            Assert.Equal(0, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
        });

        Case("已建立后 startup 专用取消不影响 running，所属身份撤销才停止", async () =>
        {
            await using var f = new Fixture();
            using var startup = new CancellationTokenSource();
            var handle = f.Handle();
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "startup-separated", startup: startup.Token);
            await Signals.Await(handle.Listening(0), "已完成启动");
            startup.Cancel();
            Assert.False(handle.RunningTokens.Single().IsCancellationRequested);
            Assert.Equal(0, handle.StopCount);
            Fixture.Revoke(f.A);
            await Signals.Await(run, "撤销 running scope");
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
        });

        Case("起播 HTTP 在飞时撤销取消报告但仍停止释放后端", async () =>
        {
            await using var f = new Fixture();
            var entered = Signals.New();
            var canceled = Signals.New();
            f.Transport.Reply = async (sent, token) =>
            {
                if (!sent.IsStart) return;
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { if (token.IsCancellationRequested) canceled.TrySetResult(); }
            };
            var handle = f.Handle();
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "start-report");
            await Signals.Await(entered.Task, "假开始 HTTP 挂起");
            Fixture.Revoke(f.A);
            await Signals.Await(canceled.Task, "开始 HTTP 已取消");
            var result = await Signals.Await(run, "后端仍完成收尾");
            Assert.False(result.ProgressReported);
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
            Assert.Equal(1, f.Finishes("start-report"));
            f.AssertSources(f.A);
        });
    }

    private static void RegisterReplacement()
    {
        foreach (var reusable in new[] { false, true })
        {
            Case("新来源接管后旧撤销及迟回调不得停止新片 reusable=" + reusable, async () =>
            {
                await using var f = new Fixture();
                var first = f.Handle(reusable);
                var next = reusable ? first : f.Handle();
                f.Backend.Next = (request, _) => Task.FromResult<IPlaybackHandle>(request.ItemId == "old" ? first : next);
                var oldRun = f.Play(f.A, "old");
                await Signals.Await(first.Listening(0), "旧来源在播");
                var lateCallbacks = first.CaptureCallbacks();
                var nextRun = f.Play(f.B, "next");
                await Signals.Await(next.Listening(reusable ? 1 : 0), "新来源已经接管");
                await Signals.Await(oldRun, "旧 operation 收尾");
                var generation = f.Service.Generation;
                var status = new ConcurrentQueue<long>();
                var tracks = new ConcurrentQueue<long>();
                var host = new ConcurrentQueue<long>();
                f.Service.StatusUpdated += value => status.Enqueue(value.Generation);
                f.Service.TracksUpdated += value => tracks.Enqueue(value.Generation);
                f.Service.VideoWindowUpdated += value => host.Enqueue(value.Generation);
                var stopsBefore = next.StopCount;
                var disposesBefore = next.DisposeCount;
                Fixture.Revoke(f.A);
                f.NotifySourcesChanged();
                lateCallbacks();
                Assert.Equal(0, status.Count);
                Assert.Equal(0, tracks.Count);
                Assert.Equal(0, host.Count);
                Assert.Equal(stopsBefore, next.StopCount);
                Assert.Equal(disposesBefore, next.DisposeCount);
                Assert.False(next.RunningTokens.Last().IsCancellationRequested);
                Assert.True(f.Service.IsPlaying);
                Assert.Equal(1, f.Finishes("old"));
                Assert.Equal(1, f.Transport.Stops("old"));
                next.Publish(7);
                Assert.Equal(generation, status.Single());
                next.End(PlaybackEndReason.UserQuit, 7);
                var result = await Signals.Await(nextRun, "新来源仍可正常完成");
                Assert.True(result.ProgressReported);
                Assert.Equal(1, next.DisposeCount);
                Assert.Equal(1, f.Finishes("next"));
                Assert.Equal(reusable ? 1 : 2, f.Backend.Starts.Count);
                Assert.Equal(reusable ? 1 : 0, first.SwapCount);
            });

            Case("新准备已撤销，旧回收与新 launch 不误借另一默认来源 reusable=" + reusable, async () =>
            {
                await using var f = new Fixture();
                var first = f.Handle(reusable);
                var third = f.Handle();
                var entered = Signals.New();
                var release = f.Hold();
                f.Transport.Reply = async (sent, _) =>
                {
                    if (sent.IsStop && sent.ItemId == "old") { entered.TrySetResult(); await release.Task; }
                };
                f.Backend.Next = (request, _) => Task.FromResult<IPlaybackHandle>(request.ItemId == "old" ? first : third);
                var oldRun = f.Play(f.A, "old");
                await Signals.Await(first.Listening(0), "旧播放");
                var canceled = f.Play(f.B, "canceled-next");
                await Signals.Await(entered.Task, "旧收尾占有 gate");
                Fixture.Revoke(f.B);
                f.Default(f.C);
                var latest = f.Play(f.C, "latest");
                release.TrySetResult();
                await Signals.Await(third.Listening(0), "最后请求进入后端");
                await Signals.Cancelled(canceled);
                await Signals.Await(oldRun, "旧播放完成");
                Fixture.Revoke(f.A);
                Assert.False(third.RunningTokens.Single().IsCancellationRequested);
                Assert.False(f.Backend.Starts.Any(request => request.ItemId == "canceled-next"));
                third.End();
                await Signals.Await(latest, "新来源成功");
                Assert.Equal(1, first.DisposeCount);
                Assert.Equal(1, third.DisposeCount);
                Assert.Equal(1, f.Finishes("old"));
            });
        }

        Case("撤销旧挂起 startup 与新 launch supersede 互守现有 gate", async () =>
        {
            await using var f = new Fixture();
            var entered = Signals.New();
            var release = f.Hold();
            var old = f.Handle();
            var next = f.Handle();
            f.Backend.Next = async (request, _) =>
            {
                if (request.ItemId != "pending-old") return next;
                entered.TrySetResult();
                await release.Task;
                return old;
            };
            var pending = f.Play(f.A, "pending-old");
            await Signals.Await(entered.Task, "旧准备持 gate");
            Fixture.Revoke(f.A);
            var latest = f.Play(f.B, "latest");
            release.TrySetResult();
            await Signals.Cancelled(pending);
            await Signals.Await(next.Listening(0), "新后端已开始");
            Assert.Equal(1, old.StopCount);
            Assert.Equal(1, old.DisposeCount);
            Assert.Equal(0, next.StopCount);
            Assert.False(next.RunningTokens.Single().IsCancellationRequested);
            next.End();
            await Signals.Await(latest, "最后来源完成");
        });
    }

    private static void RegisterShutdown()
    {
        foreach (var action in new[] { "revoke-stop", "stop-revoke", "caller-revoke", "session-dispose" })
        {
            Case("取消、停止及 session 退出恰一次清理 " + action, async () =>
            {
                await using var f = new Fixture();
                using var caller = new CancellationTokenSource();
                var handle = f.Handle();
                handle.StopHold = f.Hold();
                handle.DisposeHold = f.Hold();
                f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var run = f.Play(f.A, "shutdown", caller.Token);
                await Signals.Await(handle.Listening(0), "播放正在等退出");
                Task? stop = null;
                if (action == "stop-revoke") { stop = f.Service.StopAsync(); Fixture.Revoke(f.A); }
                else if (action == "caller-revoke") { caller.Cancel(); Fixture.Revoke(f.A); }
                else if (action == "session-dispose") f.Session.Dispose();
                else { Fixture.Revoke(f.A); stop = f.Service.StopAsync(); }
                await Signals.Await(handle.StopEntered.Task, "停止后端开始");
                Assert.False(run.IsCompleted, "收尾须等后端停止");
                stop ??= f.Service.StopAsync();
                var repeated = f.Service.StopAsync();
                Assert.False(stop.IsCompleted);
                handle.StopHold.TrySetResult();
                await Signals.Await(handle.DisposeEntered.Task, "开始释放句柄");
                Assert.False(stop.IsCompleted, "StopAsync 须等 Dispose 和报告完成");
                Assert.Equal(1, f.Finishes("shutdown"));
                handle.DisposeHold.TrySetResult();
                await Signals.Await(Task.WhenAll(run, stop, repeated), "所有等待完成");
                Assert.Equal(1, handle.StopCount);
                Assert.Equal(1, handle.DisposeCount);
                Assert.Equal(1, f.Finishes("shutdown"));
                Assert.False(f.Service.IsPlaying);
                if (action != "session-dispose") await f.NextSucceeds();
            });
        }

        Case("身份撤销不在调用线程等待同步 Stop 或异步 Dispose", async () =>
        {
            await using var f = new Fixture();
            using var barrier = new ManualResetEventSlim();
            var handle = f.Handle();
            handle.SynchronousStopBarrier = barrier;
            handle.DisposeHold = f.Hold();
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "async-cleanup");
            await Signals.Await(handle.Listening(0), "进入监视");
            var revoking = Task.Run(() => Fixture.Revoke(f.A));
            await Signals.Await(revoking, "撤销立即返回，不等待 foreign Stop");
            await Signals.Await(handle.StopEntered.Task, "后台 Stop 已开始");
            Assert.False(run.IsCompleted);
            barrier.Set();
            await Signals.Await(handle.DisposeEntered.Task, "后台 Dispose 已开始");
            Assert.False(run.IsCompleted);
            handle.DisposeHold.TrySetResult();
            await Signals.Await(run, "后台收尾完成");
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
        });

        foreach (var reason in new[] { PlaybackEndReason.EndOfFile, PlaybackEndReason.UserQuit })
        {
            Case("自然完成保留真实终因且晚撤销不变 Stop " + reason, async () =>
            {
                await using var f = new Fixture();
                var handle = f.Handle();
                var entered = Signals.New();
                var release = f.Hold();
                f.Transport.Reply = async (sent, token) =>
                {
                    if (sent.IsStop) { entered.TrySetResult(); await release.Task.WaitAsync(token); }
                };
                f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var run = f.Play(f.A, "natural");
                await Signals.Await(handle.Listening(0), "自然播放在运行");
                handle.End(reason, 12);
                await Signals.Await(entered.Task, "已确认终因，最终上报在飞");
                Fixture.Revoke(f.A);
                release.TrySetResult();
                var result = await Signals.Await(run, "保留自然终因");
                Assert.Equal(reason, result.Exit.Reason);
                Assert.Equal(0, handle.StopCount, "不能把自然 EOF 或 UserQuit 改成 client Stop");
                Assert.Equal(1, handle.DisposeCount);
                Assert.Equal(reason == PlaybackEndReason.EndOfFile ? TimeSpan.FromSeconds(100).Ticks : TimeSpan.FromSeconds(12).Ticks, result.PositionTicks);
                Assert.False(result.MarkedWatched, "已撤销身份不能补写观看历史");
                Assert.Equal(1, f.Transport.Stops("natural"));
                Assert.Equal(1, f.Finishes("natural"));
                await f.NextSucceeds();
            });
        }

        Case("撤销后 cleanup 失败不跳过 Dispose 或重复报告", async () =>
        {
            await using var f = new Fixture();
            var handle = f.Handle();
            handle.ThrowOnStop = true;
            handle.ThrowOnDispose = true;
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var run = f.Play(f.A, "cleanup-errors");
            await Signals.Await(handle.Listening(0), "后端已启动");
            Fixture.Revoke(f.A);
            await Signals.Await(run, "错误收尾仍完成");
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
            Assert.Equal(1, f.Finishes("cleanup-errors"));
            await f.NextSucceeds();
        });
    }
}
