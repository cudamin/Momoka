using System.Net;
using System.Text.Json;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.PlaybackLifecycleTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class R10ReportingTests
{
    internal static void Register()
    {
        Test("R10-006 初始轨道挂起时已看补报仍先开始后停止", () => Run(async () =>
        {
            using var f = new Fixture();
            f.Settings.Playback.StopReportEnabled = true;
            var tracks = new TaskCompletionSource<IReadOnlyList<MpvTrack>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new Handle { ReadTracks = _ => { entered.TrySetResult(); return tracks.Task; } };
            f.Backend.Next = (_, _) => Task.FromResult<Momoka.Playback.IPlaybackHandle>(handle);
            var ticket = Ticket();
            ticket.Item.RunTimeTicks = 100 * TimeSpan.TicksPerSecond;
            var play = f.Service.PlayAsync(ticket, default);
            await entered.Task;
            var manual = f.Service.ReportMarkedWatchedAsync(ticket.Item);
            tracks.TrySetResult([]);
            Assert.True(await manual, "manual stop report must succeed");
            await handle.Started.Task;
            handle.End(); await play;
            var reports = f.Transport.Requests.Where(sent => sent.IsStart || sent.IsStop).ToArray();
            Assert.Equal(2, reports.Length);
            Assert.True(reports[0].IsStart && reports[1].IsStop, "同场已看不能在开始前发停止：" + string.Join(',', reports.Select(report => report.Path)));
        }));
        Test("R10-007 旧暂停位置晚到不能覆盖较新恢复状态", () => Run(async () =>
        {
            using var f = new Fixture();
            var paused = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumed = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var handle = new Handle { ReadPosition = _ => Interlocked.Increment(ref reads) == 1 ? paused.Task : resumed.Task };
            f.Backend.Next = (_, _) => Task.FromResult<Momoka.Playback.IPlaybackHandle>(handle);
            var progress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Transport.Reply = (sent, _) => { if (sent.IsProgress) progress.TrySetResult(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); };
            var play = f.Service.PlayAsync(Ticket(), default);
            await handle.Started.Task;
            handle.Pause(true); handle.Pause(false);
            resumed.TrySetResult(10);
            await progress.Task;
            paused.TrySetResult(9);
            await Task.Delay(80);
            handle.End(); await play;
            var reports = f.Transport.Requests.Where(sent => sent.IsProgress).ToArray();
            Assert.True(reports.Length > 0);
            using var last = JsonDocument.Parse(reports[^1].Body);
            Assert.False(last.RootElement.GetProperty("IsPaused").GetBoolean());
            Assert.Equal("unpause", last.RootElement.GetProperty("EventName").GetString());
        }));
    }
    private static void Run(Func<Task> test) => test().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}
