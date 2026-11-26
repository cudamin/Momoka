using System.Collections.Concurrent;
using System.Diagnostics;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Scrobbling;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ScrobblingPlaybackTests
{
    private const long DurationTicks = TimeSpan.TicksPerSecond * 100;

    public static void Register()
    {
        Case("打格子状态：未起播不写入，真实阈值边界只写两次", async () =>
        {
            var calls = new ConcurrentQueue<bool>();
            var attempt = Attempt(calls);
            attempt.Observe(100, false);
            Assert.Equal(0, calls.Count);
            attempt.Observe(89.999, true);
            await Until(() => calls.Count == 1);
            Assert.Equal("start", Describe(calls));
            attempt.Observe(90, false);
            attempt.Observe(100, true);
            attempt.Observe(5, true);
            await attempt.FinishAsync(100, true);
            Assert.Equal("start,done", Describe(calls));
        });

        Case("打格子状态：并发观察只排在看和完成各一次", async () =>
        {
            var calls = new ConcurrentQueue<bool>();
            var attempt = Attempt(calls);
            Parallel.For(0, 128, _ => attempt.Observe(95, true));
            await attempt.FinishAsync(100, true);
            Assert.Equal("start,done", Describe(calls));
        });

        Case("打格子状态：未知位置和未知时长不能推断完成", async () =>
        {
            foreach (var position in new double?[] { null, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var calls = new ConcurrentQueue<bool>();
                var attempt = Attempt(calls);
                await attempt.FinishAsync(position, true);
                Assert.Equal("start", Describe(calls));
            }
            foreach (var duration in new long[] { 0, -1 })
            {
                var calls = new ConcurrentQueue<bool>();
                var attempt = Attempt(calls, durationTicks: duration);
                await attempt.FinishAsync(100000, true);
                Assert.Equal("start", Describe(calls));
            }
        });

        Case("打格子状态：完成请求等待在看请求，封口后拒绝迟到观察", async () =>
        {
            var calls = new ConcurrentQueue<bool>();
            var entered = Signal();
            var release = Signal();
            var attempt = new PlaybackScrobblingAttempt(DurationTicks, 90, () => true,
                async (completed, token) =>
                {
                    calls.Enqueue(completed);
                    if (!completed)
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(token);
                    }
                }, CancellationToken.None);
            attempt.Observe(5, true);
            await entered.Task;
            attempt.Observe(90, true);
            var finishing = attempt.FinishAsync(90, true);
            try
            {
                Assert.Equal("start", Describe(calls), "完成不能越过尚未结束的在看请求");
                Assert.False(finishing.IsCompleted);
            }
            finally { release.TrySetResult(); }
            await finishing;
            attempt.Observe(100, true);
            Assert.True(ReferenceEquals(finishing, attempt.FinishAsync(100, true)));
            Assert.Equal("start,done", Describe(calls));
        });

        Case("打格子状态：低进度封口后不能被迟到高进度补成完成", async () =>
        {
            var calls = new ConcurrentQueue<bool>();
            var attempt = Attempt(calls);
            await attempt.FinishAsync(5, true);
            attempt.Observe(100, true);
            await attempt.FinishAsync(100, true);
            Assert.Equal("start", Describe(calls));
        });

        Case("打格子状态：在看失败仍尝试完成，错误正文不进入失败通知", async () =>
        {
            var calls = new ConcurrentQueue<bool>();
            var notices = new ConcurrentQueue<string>();
            var attempt = new PlaybackScrobblingAttempt(DurationTicks, 90, () => true,
                (completed, _) =>
                {
                    calls.Enqueue(completed);
                    return completed ? Task.CompletedTask : Task.FromException(new IOException("synthetic-private-body"));
                }, CancellationToken.None,
                message =>
                {
                    notices.Enqueue(message);
                    throw new IOException("synthetic-notice-failure");
                });
            await attempt.FinishAsync(90, true);
            Assert.Equal("start,done", Describe(calls));
            Assert.Equal(1, notices.Count);
            Assert.DoesNotContain("synthetic-private-body", notices.Single());
        });

        Case("打格子状态：排队后撤销许可阻止完成请求", async () =>
        {
            var calls = new ConcurrentQueue<bool>();
            var entered = Signal();
            var release = Signal();
            var allowed = 1;
            var attempt = new PlaybackScrobblingAttempt(DurationTicks, 90, () => Volatile.Read(ref allowed) != 0,
                async (completed, token) =>
                {
                    calls.Enqueue(completed);
                    if (!completed)
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(token);
                    }
                }, CancellationToken.None);
            attempt.Observe(90, true);
            await entered.Task;
            Interlocked.Exchange(ref allowed, 0);
            release.TrySetResult();
            await attempt.FinishAsync(90, true);
            Assert.Equal("start", Describe(calls));
        });

        Case("打格子状态：撤销来源取消在飞请求并阻止后续完成", async () =>
        {
            using var source = new CancellationTokenSource();
            var calls = new ConcurrentQueue<bool>();
            var entered = Signal();
            var cancelled = Signal();
            var attempt = new PlaybackScrobblingAttempt(DurationTicks, 90, () => true,
                async (completed, token) =>
                {
                    calls.Enqueue(completed);
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }, source.Token);
            attempt.Observe(90, true);
            await entered.Task;
            source.Cancel();
            await cancelled.Task;
            await attempt.FinishAsync(90, true);
            Assert.Equal("start", Describe(calls));
        });

        Case("打格子状态：不响应取消的请求仍受十秒总排空限制", async () =>
        {
            var entered = Signal();
            var release = Signal();
            var attempt = new PlaybackScrobblingAttempt(DurationTicks, 90, () => true,
                async (_, _) =>
                {
                    entered.TrySetResult();
                    await release.Task;
                }, CancellationToken.None);
            attempt.Observe(5, true);
            await entered.Task;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await attempt.FinishAsync(5, true);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(13), "第三方请求即使不响应取消也不能无限阻塞播放收尾");
            }
            finally { release.TrySetResult(); }
        });

        Case("打格子播放：关闭媒体服务器上报仍按真实阈值同步", async () =>
        {
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            var handle = new PlaybackLifecycleTests.Handle();
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("threshold"), CancellationToken.None);
            await handle.Started.Task;
            try
            {
                handle.Publish(89);
                await Until(() => recording.Calls.Count == 1);
                Assert.Equal("start", recording.Describe("threshold"));
                handle.Publish(90);
                await Until(() => recording.Calls.Count == 2);
            }
            finally { handle.End(90); }
            await playing;
            Assert.Equal("start,done", recording.Describe("threshold"));
            Assert.Equal(0, fixture.Transport.Requests.Count(request => request.IsStart || request.IsProgress || request.IsStop));
            Assert.Equal("a", recording.Created.Single().UserName);
        });

        Case("打格子播放：普通片和国漫分别使用设置中的完成百分比", async () =>
        {
            foreach (var donghua in new[] { false, true })
            {
                var recording = new RecordingScrobbler();
                using var fixture = CreateFixture(recording);
                fixture.Settings.Playback.ReportProgressToServer = false;
                fixture.Settings.Playback.MarkWatchedPercent = 90;
                fixture.Settings.Playback.DonghuaMarkWatchedPercent = 85;
                var handle = new PlaybackLifecycleTests.Handle();
                fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var ticket = Episode("percent");
                if (donghua)
                {
                    ticket.Item.Genres = ["动画"];
                    ticket.Item.Studios = [new EmbyStudio { Name = "Tencent Video" }];
                }
                var playing = fixture.Service.PlayAsync(ticket, CancellationToken.None);
                await handle.Started.Task;
                handle.Publish(85);
                handle.End(85);
                await playing;
                Assert.Equal(donghua, recording.Created.Single().Request.IsDonghua);
                Assert.Equal(donghua ? "start,done" : "start", recording.Describe("percent"));
            }
        });

        Case("打格子播放：EOF不把真实低进度补满给第三方", async () =>
        {
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            var handle = new PlaybackLifecycleTests.Handle();
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("short-eof"), CancellationToken.None);
            await handle.Started.Task;
            handle.End(30, PlaybackEndReason.EndOfFile);
            var result = await playing;
            Assert.Equal(DurationTicks, result.PositionTicks, "保留原有媒体服务器EOF时间线行为");
            Assert.Equal("start", recording.Describe("short-eof"), "第三方只接受真实位置");
        });

        Case("打格子播放：结束位置缺失时有界回读真实阈值", async () =>
        {
            var reads = 0;
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            var handle = new PlaybackLifecycleTests.Handle
            {
                ReadPosition = _ =>
                {
                    Interlocked.Increment(ref reads);
                    return Task.FromResult<double?>(90);
                }
            };
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("final-read"), CancellationToken.None);
            await handle.Started.Task;
            handle.End();
            await playing;
            Assert.Equal(1, reads);
            Assert.Equal("start,done", recording.Describe("final-read"));
        });

        Case("打格子播放：没有任何真实位置时EOF也不能完成", async () =>
        {
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            var handle = new PlaybackLifecycleTests.Handle { ReadPosition = _ => Task.FromResult<double?>(null) };
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("unknown-eof"), CancellationToken.None);
            await handle.Started.Task;
            handle.Publish(5);
            handle.End(null, PlaybackEndReason.EndOfFile);
            await playing;
            Assert.Equal("start", recording.Describe("unknown-eof"));
        });

        Case("打格子播放：订阅前已出现画面时读取初始状态", async () =>
        {
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            var handle = new PlaybackLifecycleTests.Handle();
            handle.Publish(5);
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("initial"), CancellationToken.None);
            await handle.Started.Task;
            handle.End(5);
            await playing;
            Assert.Equal("start", recording.Describe("initial"));
        });

        Case("打格子播放：初始无效位置不能冒充真实起播", async () =>
        {
            foreach (var position in new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN })
            {
                var recording = new RecordingScrobbler();
                using var fixture = CreateFixture(recording);
                fixture.Settings.Playback.ReportProgressToServer = false;
                var handle = new PlaybackLifecycleTests.Handle
                {
                    Status = new PlayerStatus { Position = position },
                    ReadPosition = _ => Task.FromResult<double?>(null)
                };
                fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var playing = fixture.Service.PlayAsync(Episode("invalid-initial"), CancellationToken.None);
                await handle.Started.Task;
                handle.End();
                await playing;
                Assert.Equal(0, recording.Calls.Count);
            }
        });

        Case("打格子播放：没有状态通知时周期真实读数仍能同步", async () =>
        {
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            fixture.Service.ReportIntervalOverride = TimeSpan.FromMilliseconds(10);
            var handle = new PlaybackLifecycleTests.Handle { Position = 90 };
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("periodic"), CancellationToken.None);
            await handle.Started.Task;
            try { await Until(() => recording.Calls.Count == 2); }
            finally { handle.End(90); }
            await playing;
            Assert.Equal("start,done", recording.Describe("periodic"));
        });

        Case("打格子播放：停止播放不撤销已达阈值的第三方请求", async () =>
        {
            var entered = Signal();
            var release = Signal();
            CancellationToken completionToken = default;
            var recording = new RecordingScrobbler
            {
                Reply = async (call, token) =>
                {
                    if (!call.Completed) return;
                    completionToken = token;
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
            };
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            var handle = new PlaybackLifecycleTests.Handle { Position = 90 };
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("stop"), CancellationToken.None);
            await handle.Started.Task;
            handle.Publish(90);
            await entered.Task;
            var stopping = fixture.Service.StopAsync();
            try { Assert.False(completionToken.IsCancellationRequested, "普通停止取消播放operation，不应取消固定来源的同步"); }
            finally { release.TrySetResult(); }
            await Task.WhenAll(playing, stopping);
            Assert.Equal("start,done", recording.Describe("stop"));
        });

        Case("打格子播放：退出来源取消在飞同步且拒绝排队完成", async () =>
        {
            var entered = Signal();
            var cancelled = Signal();
            var recording = new RecordingScrobbler
            {
                Reply = async (_, token) =>
                {
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
            };
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            var handle = new PlaybackLifecycleTests.Handle();
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Episode("sign-out"), CancellationToken.None);
            await handle.Started.Task;
            handle.Publish(90);
            await entered.Task;
            fixture.Session.SignOut();
            await cancelled.Task;
            handle.End(90);
            await playing;
            Assert.Equal("start", recording.Describe("sign-out"));
        });

        Case("打格子播放：换片后旧集迟到状态不能标记新旧任一集完成", async () =>
        {
            var recording = new RecordingScrobbler();
            using var fixture = CreateFixture(recording);
            fixture.Settings.Playback.ReportProgressToServer = false;
            var first = new PlaybackLifecycleTests.Handle();
            var second = new PlaybackLifecycleTests.Handle();
            fixture.Backend.Next = (request, _) => Task.FromResult<IPlaybackHandle>(request.ItemId == "old" ? first : second);
            var old = fixture.Service.PlayAsync(Episode("old"), CancellationToken.None);
            await first.Started.Task;
            first.Publish(5);
            await Until(() => recording.Calls.Count == 1);
            var delayed = first.CaptureStatus();
            var next = fixture.Service.PlayAsync(Episode("next"), CancellationToken.None);
            await second.Started.Task;
            try
            {
                delayed(new PlayerStatus { PictureStarted = true, Loaded = true, Position = 99, Duration = 100 });
                second.Publish(5);
            }
            finally { second.End(5); }
            await Task.WhenAll(old, next);
            Assert.Equal("start", recording.Describe("old"));
            Assert.Equal("start", recording.Describe("next"));
            Assert.Equal(2, recording.Created.Count);
        });

        Case("打格子播放：音乐不创建同步场次，初始化失败不影响视频播放", async () =>
        {
            var audioRecording = new RecordingScrobbler();
            using (var fixture = CreateFixture(audioRecording))
            {
                var handle = new PlaybackLifecycleTests.Handle();
                fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var playing = fixture.Service.PlayAsync(PlaybackLifecycleTests.Ticket("audio") with { AudioOnly = true }, CancellationToken.None);
                await handle.Started.Task;
                handle.End(100);
                await playing;
                Assert.Equal(0, audioRecording.Created.Count);
                Assert.Equal(0, audioRecording.Calls.Count);
            }
            var failedRecording = new RecordingScrobbler { ThrowOnCreate = true };
            using (var fixture = CreateFixture(failedRecording))
            {
                var handle = new PlaybackLifecycleTests.Handle();
                fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var playing = fixture.Service.PlayAsync(Episode("create-failed"), CancellationToken.None);
                await handle.Started.Task;
                handle.End(90);
                var result = await playing;
                Assert.Equal(PlaybackEndReason.Stopped, result.Exit.Reason);
                Assert.Equal(0, failedRecording.Calls.Count);
            }
        });
    }

    private static PlaybackScrobblingAttempt Attempt(ConcurrentQueue<bool> calls, long durationTicks = DurationTicks) =>
        new(durationTicks, 90, () => true,
            (completed, _) =>
            {
                calls.Enqueue(completed);
                return Task.CompletedTask;
            }, CancellationToken.None);

    private static PlaybackLifecycleTests.Fixture CreateFixture(RecordingScrobbler recording)
    {
        var fixture = new PlaybackLifecycleTests.Fixture(recording);
        recording.Settings = fixture.Settings;
        return fixture;
    }

    private static PlaybackTicket Episode(string id)
    {
        var ticket = PlaybackLifecycleTests.Ticket(id);
        ticket.Item.Type = EmbyItemType.Episode;
        ticket.Item.SeriesId = "series";
        ticket.Item.SeriesName = "离线剧集";
        ticket.Item.ParentIndexNumber = 1;
        ticket.Item.IndexNumber = 1;
        return ticket;
    }

    private static string Describe(IEnumerable<bool> calls) => string.Join(",", calls.Select(completed => completed ? "done" : "start"));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (condition()) return;
            await Task.Delay(5);
        }
        throw new TimeoutException("等待离线观看记录同步");
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => run().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult());
    private sealed record SyncCall(string ItemId, string SourceKey, bool Completed);
    private sealed record CreatedAttempt(string ItemId, string SourceKey, string UserName, PlaybackRequest Request);

    private sealed class RecordingScrobbler : IPlaybackScrobbler
    {
        public AppSettings Settings { get; set; } = new();
        public ConcurrentQueue<SyncCall> Calls { get; } = new();
        public ConcurrentQueue<CreatedAttempt> Created { get; } = new();
        public Func<SyncCall, CancellationToken, Task> Reply { get; init; } = (_, _) => Task.CompletedTask;
        public bool ThrowOnCreate { get; init; }

        public PlaybackScrobblingAttempt? CreateAttempt(PlaybackTicket ticket, EmbySessionScope scope, PlaybackRequest request)
        {
            if (ThrowOnCreate) throw new IOException("synthetic-create-failure");
            Created.Enqueue(new CreatedAttempt(ticket.Item.Id, scope.Key, scope.UserName, request));
            var percent = PlaybackService.StopReportPercent(request.IsDonghua,
                Settings.Playback.MarkWatchedPercent, Settings.Playback.DonghuaMarkWatchedPercent);
            return new PlaybackScrobblingAttempt(request.RunTimeTicks, percent, () => scope.IsAvailable,
                async (completed, token) =>
                {
                    var call = new SyncCall(ticket.Item.Id, scope.Key, completed);
                    Calls.Enqueue(call);
                    await Reply(call, token);
                }, scope.Lifetime);
        }

        public string Describe(string itemId) => ScrobblingPlaybackTests.Describe(Calls.Where(call => call.ItemId == itemId).Select(call => call.Completed));
    }
}
