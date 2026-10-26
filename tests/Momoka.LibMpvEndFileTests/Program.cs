using System.Runtime.InteropServices;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("R048 独立离线夹具不接受参数；不会回退启动全 Core/SelfCheck 或播放器。");
            return 2;
        }
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Register();
        var passed = 0;
        foreach (var (name, run) in Cases)
        {
            try { await run(); passed++; Console.WriteLine("  通过  " + name); }
            catch (Exception error)
            {
                Console.WriteLine("  失败  " + name);
                Console.WriteLine(error is AssertionException ? error.Message : error.ToString());
            }
        }
        var failed = Cases.Count - passed;
        Console.WriteLine($"共 {Cases.Count} 项，通过 {passed}，失败 {failed}，跳过 0");
        Console.WriteLine($"实际匿名假 HTTP 请求 {ReportBoundary.TotalSent} 次；仅托管 native seam，无原生初始化或媒体加载。");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Func<Task> run) => Cases.Add((name, run));

    private static void Register()
    {
        Test("ABI：固定 x64 payload 与实际 managed reason/error 前缀一致", () =>
        {
            Assert.Equal(8, IntPtr.Size);
            Assert.Equal(32, Marshal.SizeOf<HandleFixture.PinnedEndFile>());
            Assert.Equal(0L, Marshal.OffsetOf<LibMpvNative.MpvEventEndFile>("Reason").ToInt64());
            Assert.Equal(4L, Marshal.OffsetOf<LibMpvNative.MpvEventEndFile>("Error").ToInt64());
            Assert.Equal(5, LibMpvNative.EndFileRedirect);
            return Task.CompletedTask;
        });
        foreach (var standalone in new[] { false, true })
        {
            var mode = standalone ? "独占边界" : "合成边界";
            Test(mode + "：REDIRECT 保持 Exit pending、位置和输出、不发 quit/destroy", async () =>
            {
                await using var f = new HandleFixture(standalone);
                f.Ready();
                var published = f.Published.Count;
                Assert.False(f.End(5, insert: 0x1234567800000010, count: 3));
                Assert.False(f.Exit.IsCompleted, "playlist expansion 不能完成当前播放任务");
                Assert.False(f.Waiter.IsCompleted);
                Assert.Equal(12.5, f.Handle.Status.Position);
                Assert.Equal(12.5, await f.Handle.GetPositionAsync(CancellationToken.None));
                Assert.Equal(published, f.Published.Count);
                Assert.Equal(0, f.Surface.DetachCalls);
                Assert.Equal(0, f.Surface.CaptureClearCalls);
                Assert.Equal(0, f.Native.QuitCalls);
                Assert.Equal(0, f.Native.DestroyCalls);
                if (!standalone)
                {
                    f.Dispatch(LibMpvNative.EventPropertyChange, userData: 10);
                    Assert.Equal(new IntPtr(777), f.Surface.Chains.Last());
                    Assert.Equal(0, f.Surface.DetachCalls, "合成闸仍允许真实输出刷新方法执行");
                }
            });
            Test(mode + "：REDIRECT 后 start/file-loaded/position 继续，实际 EOF 才结束", async () =>
            {
                await using var f = new HandleFixture(standalone);
                f.Ready(3);
                f.End(5, insert: 202, count: 2);
                f.Dispatch(LibMpvNative.EventStartFile);
                f.Dispatch(LibMpvNative.EventFileLoaded);
                f.Number(1, 24.75);
                f.Dispatch(LibMpvNative.EventPlaybackRestart);
                Assert.False(f.Exit.IsCompleted);
                Assert.True(f.Handle.Status.Loaded);
                Assert.True(f.Handle.Status.PictureStarted);
                Assert.Equal(24.75, await f.Handle.GetPositionAsync(CancellationToken.None));
                Assert.False(f.End(0));
                var exit = await f.Exit.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(PlaybackEndReason.EndOfFile, exit.Reason);
                Assert.Equal(24.75, exit.PositionSeconds);
                Assert.False(exit.IsFailure);
                Assert.Equal(exit, await f.Waiter.WaitAsync(TimeSpan.FromSeconds(2)));
            });
        }
        Test("实际 managed EndFile 结构：reason 5 也不能结束", async () =>
        {
            await using var f = new HandleFixture();
            f.ManagedEnd(LibMpvNative.EndFileRedirect);
            Assert.False(f.Exit.IsCompleted);
            Assert.Equal(null, await f.Handle.GetPositionAsync(CancellationToken.None));
            f.ManagedEnd(LibMpvNative.EndFileEof);
            Assert.Equal(PlaybackEndReason.EndOfFile, (await f.Exit).Reason);
        });
        Test("连续/空 expansion：未 Loaded 的未知位置保留，最终 Shutdown 正常清理", async () =>
        {
            await using var f = new HandleFixture();
            for (var index = 0; index < 4; index++)
            {
                f.End(5, entry: 100 + index, insert: index == 0 ? 0 : 200 + index, count: index == 0 ? 0 : 2);
                f.Dispatch(LibMpvNative.EventStartFile);
                Assert.False(f.Exit.IsCompleted);
                Assert.False(f.Handle.Status.Loaded);
                Assert.Equal(null, await f.Handle.GetPositionAsync(CancellationToken.None));
                Assert.Equal(0, f.Surface.DetachCalls);
            }
            Assert.True(f.Dispatch(LibMpvNative.EventShutdown));
            Assert.Equal(PlaybackEndReason.UserQuit, (await f.Exit).Reason);
            await f.DisposeAsync();
            await f.DisposeAsync();
            Assert.Equal(1, f.Native.DestroyCalls);
            Assert.Equal(1, f.Surface.DetachCalls);
            Assert.True(f.LeaveLoop);
        });
        Test("原行为对照：无 redirect 的 EOF/Stop/Quit/Error 与未 Loaded Shutdown 不变", async () =>
        {
            foreach (var terminal in new[] { 0, 2, 3, 4 })
            {
                await using var f = new HandleFixture();
                f.Ready(4.5);
                f.End(terminal, terminal == 4 ? -13 : 0);
                var exit = await f.Exit.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(terminal == 0 ? PlaybackEndReason.EndOfFile
                    : terminal == 4 ? PlaybackEndReason.Error : PlaybackEndReason.UserQuit, exit.Reason);
                Assert.Equal(4.5, exit.PositionSeconds);
            }
            await using var unloaded = new HandleFixture();
            Assert.True(unloaded.Dispatch(LibMpvNative.EventShutdown));
            Assert.Equal(PlaybackEndReason.UserQuit, (await unloaded.Exit).Reason);
            Assert.Equal(null, (await unloaded.Exit).PositionSeconds);
        });
        Test("实际 swap 等待期：旧 redirect 过滤但 Shutdown 必须完成新任务并清理", async () =>
        {
            await using var f = new HandleFixture(standalone: true);
            f.Ready(20);
            f.Handle.HandOver();
            Assert.True(await f.Handle.SwapToAsync(HandleFixture.Request(), CancellationToken.None));
            var nextExit = f.Field<TaskCompletionSource<PlaybackExit>>("_exit").Task;
            f.End(5);
            Assert.True(f.Swapping);
            Assert.False(nextExit.IsCompleted);
            Assert.True(f.Dispatch(LibMpvNative.EventShutdown));
            Assert.Equal(PlaybackEndReason.UserQuit, (await nextExit).Reason);
            await f.DisposeAsync();
            Assert.Equal(1, f.Native.DestroyCalls);
        });
        foreach (var terminal in new[] { 0, 2, 3, 4 })
        {
            Test($"终止 reason={terminal}：REDIRECT 后原分类、位置和 error 不变", async () =>
            {
                await using var f = new HandleFixture();
                f.Ready(8.25);
                f.End(5, insert: 202, count: 1);
                f.End(terminal, terminal == 4 ? -13 : 0);
                var exit = await f.Exit.WaitAsync(TimeSpan.FromSeconds(2));
                var expected = terminal == 0 ? PlaybackEndReason.EndOfFile
                    : terminal == 4 ? PlaybackEndReason.Error : PlaybackEndReason.UserQuit;
                Assert.Equal(expected, exit.Reason);
                Assert.Equal(8.25, exit.PositionSeconds);
                Assert.Equal(terminal == 4 ? -13 : 0, exit.ExitCode);
                Assert.Equal(terminal == 4, exit.IsFailure);
                Assert.Equal(1, f.Surface.DetachCalls);
                if (terminal == 4) Assert.Contains("-13", exit.Message ?? "");
            });
        }
        foreach (var terminal in new[] { LibMpvNative.EndFileStop, LibMpvNative.EndFileQuit, -1 })
        {
            Test($"主动 StopAsync：REDIRECT 后 {(terminal == -1 ? "Shutdown" : "reason=" + terminal)} 仍 Stopped", async () =>
            {
                await using var f = new HandleFixture();
                f.Ready(9);
                f.End(5);
                f.Native.OnCommand = command =>
                {
                    if (command[0] != "quit") return;
                    if (terminal == -1) f.Dispatch(LibMpvNative.EventShutdown);
                    else f.End(terminal);
                };
                var stop = f.Handle.StopAsync();
                Assert.True(ReferenceEquals(stop, f.Handle.StopAsync()));
                await stop.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(PlaybackEndReason.Stopped, (await f.Exit).Reason);
                Assert.Equal(1, f.Native.QuitCalls);
                Assert.Equal(9d, (await f.Exit).PositionSeconds);
            });
        }
        foreach (var oldReason in new[] { 0, 2, 3, 4, 5 })
        {
            Test($"实际 HandOver/Swap：旧 reason={oldReason} 过滤，新连续 REDIRECT 后 EOF 结束新任务", async () =>
            {
                await using var f = new HandleFixture(standalone: true);
                f.Ready(60);
                var request = HandleFixture.Request();
                Assert.True(f.Handle.CanSwapTo(request));
                f.Handle.HandOver();
                var oldExit = await f.Exit;
                Assert.Equal(PlaybackEndReason.Stopped, oldExit.Reason);
                Assert.True(await f.Handle.SwapToAsync(request, CancellationToken.None));
                var nextExit = f.Field<TaskCompletionSource<PlaybackExit>>("_exit").Task;
                var nextWaiter = f.Handle.WaitForExitAsync(CancellationToken.None);
                Assert.True(f.Swapping);
                f.End(oldReason, oldReason == 4 ? -13 : 0);
                f.Dispatch(LibMpvNative.EventFileLoaded);
                f.Number(1, 98);
                Assert.True(f.Swapping);
                Assert.False(nextExit.IsCompleted);
                f.Dispatch(LibMpvNative.EventStartFile);
                Assert.False(f.Swapping);
                Assert.Equal(-1d, f.Handle.Status.Position);
                Assert.Equal(null, await f.Handle.GetPositionAsync(CancellationToken.None));
                f.End(5, insert: 202, count: 2);
                f.Dispatch(LibMpvNative.EventStartFile);
                f.End(5, entry: 202, insert: 303, count: 1);
                f.Dispatch(LibMpvNative.EventStartFile);
                f.Dispatch(LibMpvNative.EventFileLoaded);
                f.Number(1, 7.5);
                Assert.False(nextExit.IsCompleted);
                Assert.Equal(1, f.Native.LoadCalls);
                f.End(0);
                Assert.Equal(PlaybackEndReason.EndOfFile, (await nextExit).Reason);
                Assert.Equal(7.5, (await nextExit).PositionSeconds);
                Assert.Equal(await nextExit, await nextWaiter);
                Assert.Equal(60d, oldExit.PositionSeconds);
            });
        }
        Test("取消一个 Exit 等待者不取消 redirect 后生命周期，另一个继续等实际 EOF", async () =>
        {
            await using var f = new HandleFixture();
            using var cancellation = new CancellationTokenSource();
            var waiter = f.Handle.WaitForExitAsync(cancellation.Token);
            f.End(5);
            cancellation.Cancel();
            try { await waiter; throw new AssertionException("取消等待应抛 OperationCanceledException"); }
            catch (OperationCanceledException) { }
            Assert.False(f.Exit.IsCompleted);
            f.End(0);
            Assert.Equal(PlaybackEndReason.EndOfFile, (await f.Exit).Reason);
        });
        foreach (var kind in new[] { Momoka.Emby.MediaServerKind.Emby, Momoka.Emby.MediaServerKind.Jellyfin })
        {
            Test($"实际 Service/{kind} 捕获来源：redirect 不补满/已看/释放，EOF 后才调用真实 VM next 判定", async () =>
            {
                await using var f = new ServiceFixture(kind);
                using var vm = new AutoNextBoundary();
                vm.Settings.Playback.AutoPlayNextEpisode = true;
                var following = f.Target.Bind(new Momoka.Emby.EmbyItem
                { Id = "next", Type = "Episode", SeasonId = "season" });
                vm.Episodes.AddRange([f.Item, following]);
                await f.StartAsync();
                var play = f.Play!;
                async Task<EpisodeDestination?> ConsumeAsync() => vm.Next(await play, f.Item);
                var consumer = ConsumeAsync();
                f.ChangeDefault();
                f.Backend.End(5, insert: 202, count: 2);
                Assert.False(f.Backend.Exit.IsCompleted, "真实 handle 的当前完成源仍 pending");
                Assert.False(play.IsCompleted, "Service 仍在 Monitor，不进入 final report/release");
                Assert.False(consumer.IsCompleted, "VM 不应收到假 EOF 并接集");
                Assert.Equal(0, vm.Decisions);
                Assert.True(f.Service.IsPlaying);
                Assert.Equal(1, f.NowPlaying.Count);
                Assert.Equal(1, f.Transport.Requests.Count);
                Assert.Equal("r048-target.invalid", f.Transport.Requests.Single().Host);
                Assert.False(f.Transport.Requests.Any(sent => sent.IsStop || sent.IsWatched));
                Assert.True(f.Backend.StatusListeners >= 3, "真实 Service 状态/阈值订阅还在");
                Assert.Equal(0, f.Backend.Native.DestroyCalls);
                Assert.Equal(0, f.Backend.Surface.DetachCalls);
                f.Backend.Dispatch(LibMpvNative.EventStartFile);
                f.Backend.Dispatch(LibMpvNative.EventFileLoaded);
                f.Backend.Number(1, 30);
                Assert.False(play.IsCompleted);
                f.Backend.End(0);
                var result = await play.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(PlaybackEndReason.EndOfFile, result.Exit.Reason);
                Assert.Equal(TimeSpan.TicksPerSecond * 100, result.PositionTicks);
                Assert.True(result.MarkedWatched);
                Assert.True(result.ProgressReported);
                Assert.Equal(1, f.Transport.Requests.Count(sent => sent.IsStop));
                Assert.Equal(1, f.Transport.Requests.Count(sent => sent.IsWatched));
                Assert.True(f.Transport.Requests.All(sent => sent.Host == "r048-target.invalid"));
                var stop = f.Transport.Requests.Single(sent => sent.IsStop);
                Assert.Equal(TimeSpan.TicksPerSecond * 100, stop.Field("PositionTicks").GetInt64());
                Assert.Equal("episode", stop.Field("ItemId").GetString());
                Assert.Equal(1, f.Backend.Native.DestroyCalls);
                Assert.Equal(1, f.Backend.Surface.DetachCalls);
                Assert.Equal(1, f.Backend.StatusListeners, "Service 已撤订，只剩夹具监听");
                Assert.False(f.Service.IsPlaying);
                Assert.Equal(1, f.NowPlaying.Count(item => item is null));
                Assert.True(ReferenceEquals(following, (await consumer)?.Episode));
                Assert.Equal(1, vm.Decisions);
                Assert.Equal(1, f.BackendStarts);
            });
        }
        Test("实际 Service：redirect 后 timer/position 继续，不补满；Error 保留失败且 VM 不接集", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.ProgressReportIntervalSeconds = 1;
            using var vm = new AutoNextBoundary();
            vm.Settings.Playback.AutoPlayNextEpisode = true;
            vm.Episodes.AddRange([f.Item, f.Target.Bind(new Momoka.Emby.EmbyItem
            { Id = "next", Type = "Episode", SeasonId = "season" })]);
            await f.StartAsync();
            f.Backend.End(5);
            Assert.False(f.Backend.Exit.IsCompleted);
            Assert.False(f.Play!.IsCompleted);
            f.Backend.Number(1, 17);
            await f.Transport.Progressed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var progress = f.Transport.Requests.Last(sent => sent.IsProgress);
            Assert.Equal(TimeSpan.TicksPerSecond * 17, progress.Field("PositionTicks").GetInt64());
            Assert.False(f.Transport.Requests.Any(sent => sent.IsStop || sent.IsWatched));
            f.Backend.End(4, -13);
            var result = await f.Play.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(PlaybackEndReason.Error, result.Exit.Reason);
            Assert.Equal(TimeSpan.TicksPerSecond * 17, result.PositionTicks);
            Assert.False(result.MarkedWatched);
            Assert.True(f.Transport.Requests.Single(sent => sent.IsStop).Field("Failed").GetBoolean());
            Assert.Equal(null, vm.Next(result, f.Item));
            Assert.Equal(1, f.Backend.Native.DestroyCalls);
        });
        Test("实际 Service：redirect 后来源撤销不借新默认账号，真实 stop 仍清理", async () =>
        {
            await using var f = new ServiceFixture();
            await f.StartAsync();
            f.Backend.End(5);
            Assert.False(f.Backend.Exit.IsCompleted);
            Assert.False(f.Play!.IsCompleted);
            f.ChangeDefault();
            f.RevokeTarget();
            await f.Service.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var result = await f.Play.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(PlaybackEndReason.Stopped, result.Exit.Reason);
            Assert.False(result.ProgressReported);
            Assert.False(result.MarkedWatched);
            Assert.Equal(1, f.Transport.Requests.Count);
            Assert.Equal("r048-target.invalid", f.Transport.Requests.Single().Host);
            Assert.Equal(1, f.Backend.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Test("实际 EOF 后再到 redirect/shutdown：首个 Exit 不覆盖，dispose/detach 只一次", async () =>
        {
            await using var f = new HandleFixture();
            f.Ready(80);
            f.End(0);
            var exit = await f.Exit;
            f.End(5);
            Assert.True(f.Dispatch(LibMpvNative.EventShutdown));
            Assert.Equal(exit, await f.Exit);
            await f.DisposeAsync();
            await f.DisposeAsync();
            Assert.Equal(1, f.Native.DestroyCalls);
            Assert.Equal(1, f.Surface.DetachCalls);
            Assert.False(await f.Handle.CommandAsync(["set", "pause", "yes"], CancellationToken.None));
            Assert.Equal(0, f.Native.QuitCalls);
        });
    }
}
