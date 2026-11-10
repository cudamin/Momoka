using System.Collections.Concurrent;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MusicPlaybackTests
{
    public static void Register()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"音乐计划：{kind} 使用音频静态流并保留来源身份", () =>
            {
                var settings = new AppSettings();
                settings.Audio.Volume = 37;
                settings.Audio.DelayMilliseconds = 250;
                settings.Mpv.SvpEnabled = true;
                settings.Mpv.SvpVapourSynthPath = "unused";
                var ticket = Ticket();
                var request = Planner(settings).Plan(ticket, Connection(kind));
                Assert.Equal("/emby/Audio/song/stream.flac", request.MediaUrl.AbsolutePath);
                Assert.Contains("Static=true", request.MediaUrl.Query);
                Assert.Contains("MediaSourceId=source", request.MediaUrl.Query);
                Assert.True(request.AudioOnly);
                Assert.False(request.SvpEnabled);
                Assert.Equal(0, request.ExternalSubtitles.Count);
                Assert.True(request.SubtitlesDisabled);
                Assert.Null(request.ShaderDecision);
                Assert.Equal(0, request.ShaderOptionCount);
                Assert.Equal("37", Option(request.PlayerOptions, "volume"));
                Assert.Equal("0.25", Option(request.PlayerOptions, "audio-delay"));
                Assert.False(request.PlayerOptions.Any(option => option.Key is "vo" or "gpu-api" or "profile"
                    or "glsl-shaders" or "sub-font" or "vf"));
                Assert.Equal("song", request.ItemId);
                Assert.Equal("source", request.MediaSourceId);
            });
        }

        Test("音乐计划：同源原始音频地址优先且不改变身份与请求头", () =>
        {
            var ticket = Ticket();
            ticket.Source.DirectStreamUrl = new Uri("https://music.invalid/emby/audio/file?id=42");
            var request = Planner(new AppSettings()).Plan(ticket, Connection());
            Assert.Equal(ticket.Source.DirectStreamUrl, request.MediaUrl);
            Assert.True(request.HttpHeaders.Any(pair => pair.Value.Contains("offline-token", StringComparison.Ordinal)));
            Assert.Equal(TimeSpan.TicksPerSecond * 180, request.RunTimeTicks);
        });

        foreach (var address in new[] { "https://foreign.invalid/song.flac", "file:///C:/song.flac",
            "https://name:secret@music.invalid/song.flac", "https://music.invalid/song.flac?api_key=secret" })
        {
            Test($"音乐计划：拒绝越界或带凭据的原始地址（{address.Split('?')[0]}）", () =>
            {
                var ticket = Ticket();
                ticket.Source.DirectStreamUrl = new Uri(address);
                Assert.Throws<InvalidOperationException>(() => Planner(new AppSettings()).Plan(ticket, Connection()));
            });
        }

        foreach (var pipeline in new[] { VideoPipelineKind.Integrated, VideoPipelineKind.Standalone })
        {
            Test($"音乐原生契约：{pipeline} 不要求视频面板且钉死无窗输出", () =>
            {
                Assert.Null(LibMpvBackend.SelectSurface(pipeline, () => throw new AssertionException("音乐不能请求视频面板"), audioOnly: true));
                var options = LibMpvPipelinePolicy.BuildAudioOnly(
                    [new("vo", "gpu-next"), new("force-window", "immediate"), new("volume", "37")]);
                Assert.Equal("null", options.Last(option => option.Name == "vo").Value);
                Assert.Equal("no", options.Last(option => option.Name == "video").Value);
                Assert.Equal("no", options.Last(option => option.Name == "force-window").Value);
                Assert.Equal("no", options.Last(option => option.Name == "fullscreen").Value);
                Assert.False(options.Any(option => option.Name is "gpu-api" or "gpu-context" or "wid" or "d3d11-composition-size"));
                Assert.Equal("37", options.Single(option => option.Name == "volume").Value);
                Assert.True(options.Where(option => option.Name is "vo" or "video" or "force-window" or "fullscreen").All(option => option.Required));
            });
        }

        Test("音乐外部参数：无窗契约覆盖请求中的视频值与全屏意图", () =>
        {
            var request = new PlaybackRequest
            {
                MediaUrl = new Uri("https://music.invalid/song.flac"),
                Title = "歌曲",
                Fullscreen = true,
                PlayerOptions = [new("vo", "gpu-next"), new("force-window", "yes")],
                AudioOnly = true
            };
            var args = MpvArgumentBuilder.Build(request, "offline-pipe");
            Assert.Equal("--vo=null", args.Last(argument => argument.StartsWith("--vo=", StringComparison.Ordinal)));
            Assert.Equal("--force-window=no", args.Last(argument => argument.StartsWith("--force-window=", StringComparison.Ordinal)));
            Assert.Equal("--fullscreen=no", args.Last(argument => argument.StartsWith("--fullscreen=", StringComparison.Ordinal)));
        });

        Case("音乐生命周期：所有更新与最后空条目仍携带音频代次", async () =>
        {
            using var fixture = new PlaybackLifecycleTests.Fixture();
            var updates = new ConcurrentQueue<PlaybackUpdate<EmbyItem?>>();
            var statuses = new ConcurrentQueue<PlaybackUpdate<PlayerStatus>>();
            var progresses = new ConcurrentQueue<PlaybackProgress>();
            var handle = new PlaybackLifecycleTests.Handle();
            fixture.Service.NowPlayingUpdated += updates.Enqueue;
            fixture.Service.StatusUpdated += statuses.Enqueue;
            fixture.Service.ProgressChanged += progresses.Enqueue;
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Ticket(), CancellationToken.None);
            await handle.Started.Task;
            handle.Publish(17);
            handle.Pause(true);
            await Until(() => !progresses.IsEmpty);
            handle.End(17);
            await playing;
            Assert.True(updates.Count >= 2);
            Assert.True(updates.All(update => update.AudioOnly));
            Assert.Null(updates.Last().Value);
            Assert.True(statuses.All(update => update.AudioOnly));
            Assert.True(progresses.All(update => update.AudioOnly));
        });

        Test("音乐原生句柄：不允许交换视频或保留结束上下文", () =>
        {
            var handle = new LibMpvHandle(IntPtr.Zero, null, null, null) { AudioOnly = true };
            Assert.False(handle.CanSwapTo(new PlaybackRequest { AudioOnly = true, MediaUrl = new Uri("https://music.invalid/next.flac"), Title = "下一首" }));
            Assert.False(handle.TryRetainAfterEndOfFile());
            Assert.Null(handle.PictureInHostWindow);
        });

        Test("音乐原生事件：纯音频加载不触发视频窗口兜底", () =>
        {
            // Destruction gate prevents native calls. The real FileLoaded path must still publish
            // music loading and leave its force-window latch untouched.
            var handle = new LibMpvHandle(IntPtr.Zero, null, null, null) { AudioOnly = true };
            var gate = (LibMpvLifetimeGate)typeof(LibMpvHandle).GetField("_apiGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handle)!;
            gate.Destroy(() => { });
            typeof(LibMpvHandle).GetMethod("ProcessEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(handle, [new LibMpvNative.MpvEvent { EventId = LibMpvNative.EventFileLoaded }]);
            Assert.False((bool)typeof(LibMpvHandle).GetField("_windowHeld", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handle)!);
            Assert.True(handle.Status.Loaded);
        });

        Case("音乐上报：影视阈值与已观看写入不介入歌曲会话", async () =>
        {
            using var fixture = new PlaybackLifecycleTests.Fixture();
            fixture.Settings.Playback.StopReportEnabled = true;
            fixture.Settings.Playback.MarkWatchedPercent = 50;
            var handle = new PlaybackLifecycleTests.Handle();
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Ticket(), CancellationToken.None);
            await handle.Started.Task;
            handle.Publish(179);
            Assert.False(fixture.Transport.Requests.Any(request => request.IsStop));
            handle.End(180, PlaybackEndReason.EndOfFile);
            var result = await playing;
            Assert.False(result.MarkedWatched);
            Assert.Equal(1, fixture.Transport.Requests.Count(request => request.IsStart));
            Assert.Equal(1, fixture.Transport.Requests.Count(request => request.IsStop));
            Assert.False(fixture.Transport.Requests.Any(request => request.Path.Contains("PlayedItems", StringComparison.Ordinal)));
        });

        Case("音乐生命周期：外部视频设置仍选择内置音频后端并禁止跨类型交换", async () =>
        {
            using var fixture = new PlaybackLifecycleTests.Fixture();
            fixture.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            var musicBackend = new PlaybackLifecycleTests.Backend();
            var audio = new PlaybackLifecycleTests.Handle { Swappable = true };
            var video = new PlaybackLifecycleTests.Handle();
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(video);
            musicBackend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(audio);
            var service = new PlaybackService(fixture.Session, fixture.Settings, () => fixture.Backend,
                Planner(fixture.Settings), audioBackendFactory: () => musicBackend);
            var playing = service.PlayAsync(Ticket(), CancellationToken.None);
            await audio.Started.Task;
            Assert.True(service.IsAudioOnly);
            Assert.Equal(MpvBackendKind.BuiltInLibMpv, service.PlayingBackend);
            Assert.Equal(0, fixture.Backend.Starts.Count);
            var next = service.PlayAsync(PlaybackLifecycleTests.Ticket("video"), CancellationToken.None);
            await video.Started.Task;
            Assert.False(service.IsAudioOnly);
            Assert.Equal(0, audio.SwapCount);
            Assert.Equal(1, audio.StopCount);
            video.End();
            await Task.WhenAll(playing, next);
        });

        Case("音乐生命周期：无控制通道失败会清理句柄且不发送播放开始", async () =>
        {
            using var fixture = new PlaybackLifecycleTests.Fixture();
            var handle = new PlaybackLifecycleTests.Handle { HasControlChannel = false };
            fixture.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var playing = fixture.Service.PlayAsync(Ticket(), CancellationToken.None);
            try { await playing; throw new AssertionException("应拒绝无控制音乐播放器"); }
            catch (InvalidOperationException) { }
            Assert.Equal(1, handle.StopCount);
            Assert.Equal(1, handle.DisposeCount);
            Assert.False(fixture.Transport.Requests.Any(request => request.IsStart));
            Assert.False(fixture.Service.IsPlaying);
        });
    }

    private static PlaybackTicket Ticket()
    {
        var source = new MediaSource { Id = "source", Container = "flac", RunTimeTicks = TimeSpan.TicksPerSecond * 180 };
        var item = new EmbyItem { Id = "song", Name = "歌曲", Type = "Audio", MediaSources = [source] };
        return new PlaybackTicket { Item = item, Source = source, AudioOnly = true };
    }

    private static PlaybackPlanner Planner(AppSettings settings) => new(settings, new ShaderGroupResolver(settings.Shaders));
    private static EmbyConnection Connection(MediaServerKind kind = MediaServerKind.Emby) =>
        new(new Uri("https://music.invalid/emby/"), "offline-token", "user", "user", "music", DeviceIdentity.Create("offline", "1")) { ServerKind = kind };
    private static string? Option(IEnumerable<KeyValuePair<string, string>> options, string name) => options.LastOrDefault(pair => pair.Key == name).Value;
    private static void Case(string name, Func<Task> action) => Test(name, () => action().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition()) return;
            await Task.Delay(5);
        }
        throw new TimeoutException("等待音乐状态上报");
    }
}
