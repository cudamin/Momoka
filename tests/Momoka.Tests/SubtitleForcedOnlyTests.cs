using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>Forced-only planning and fake-backend regressions; no real server, decoder, native DLL or media.</summary>
internal static class SubtitleForcedOnlyTests
{
    public static void Register()
    {
        foreach (var audioLanguage in new[] { "chi", "jpn", "" })
            TestHarness.Test($"R-057 缺字幕元数据/音轨{audioLanguage}时只强制仍关闭", () =>
            {
                var settings = Settings(SubtitleMode.ForcedOnly);
                var source = Source(audioLanguage.Length == 0 ? [] : [Audio(audioLanguage)]);
                var choice = TrackSelection.Resolve(settings.Playback, source).Subtitle;
                Assert.True(choice.Disabled, "缺元数据不能把仅强制退回普通auto");
                Assert.Null(choice.Stream);
                Assert.True(Planner(settings).SuggestTracks(source).Subtitle.Disabled);
            });
        foreach (var languages in new IReadOnlyList<string>[] { ["中文"], [TrackLanguagePriority.Any], [] })
            foreach (var fallback in new[] { false, true })
                TestHarness.Test($"R-057 未知字幕仅强制，语言{string.Join('/', languages)}兜底{fallback}不放宽", () =>
                {
                    var settings = Settings(SubtitleMode.ForcedOnly);
                    settings.Playback.SubtitleLanguages = languages.ToList();
                    settings.Playback.SubtitleFallbackToDefault = fallback;
                    Assert.True(TrackSelection.Resolve(settings.Playback, Source()).Subtitle.Disabled);
                });
        TestHarness.Test("R-057 无字幕信息的规划票明确关闭、无普通自动ID", () =>
        {
            var request = Plan(Settings(SubtitleMode.ForcedOnly), Source());
            Assert.True(request.SubtitlesDisabled);
            Assert.Null(request.SubtitleId);
            Assert.Null(request.SubtitleStreamIndex);
            Assert.Equal(-1, new PlaybackTrackState(Source(), request).Selection.Subtitle);
        });
        TestHarness.Test("R-057 外部后端参数与同窗换片都发送sid=no，不启动进程", () =>
        {
            var request = Plan(Settings(SubtitleMode.ForcedOnly), Source());
            var arguments = MpvArgumentBuilder.Build(request, @"\\.\pipe\fixture");
            Assert.True(arguments.Contains("--sid=no", StringComparer.Ordinal));
            Assert.Equal("no", InlineSwitch.PerFile(request).First(pair => pair.Key == "sid").Value);
        });
        TestHarness.Test("R-057 详情自动行说明关闭，没有字幕时保留手动关闭入口", () =>
        {
            var rows = ItemDetail.SubtitleRows(Settings(SubtitleMode.ForcedOnly).Playback, Source());
            Assert.Equal(2, rows.Count);
            Assert.Contains("不显示", rows[0].Text);
            Assert.True(rows[1].Disable);
        });
        TestHarness.Test("R-057 已知普通与强制混合轨，自动仅选可加载强制轨", () =>
        {
            var settings = Settings(SubtitleMode.ForcedOnly);
            var source = Source(Subtitle(2), Subtitle(3, forced: true));
            source.DefaultSubtitleStreamIndex = 2;
            var request = Plan(settings, source);
            Assert.False(request.SubtitlesDisabled);
            Assert.Equal(3, request.SubtitleStreamIndex);
            Assert.Equal(2, request.SubtitleId);
        });
        TestHarness.Test("R-057 已知普通默认轨不突破强制模式，缺codec外部轨也不自动", () =>
        {
            var source = Source(Subtitle(2));
            source.DefaultSubtitleStreamIndex = 2;
            Assert.True(Plan(Settings(SubtitleMode.ForcedOnly), source).SubtitlesDisabled);
            var forced = Subtitle(3, forced: true);
            forced.IsExternal = true;
            forced.Codec = "pgs";
            Assert.True(Plan(Settings(SubtitleMode.ForcedOnly), Source(forced)).SubtitlesDisabled);
        });
        TestHarness.Test("R-057 显式选择普通字幕优先于仅强制，显式关闭仍最高", () =>
        {
            var settings = Settings(SubtitleMode.ForcedOnly);
            var source = Source(Subtitle(2), Subtitle(3, forced: true));
            var ticket = Ticket(source) with { SubtitleStreamIndex = 2 };
            var planner = Planner(settings);
            var explicitTrack = planner.Plan(ticket, Connection());
            Assert.False(explicitTrack.SubtitlesDisabled);
            Assert.Equal(2, explicitTrack.SubtitleStreamIndex);
            Assert.Equal(1, explicitTrack.SubtitleId);
            var disabled = planner.Plan(ticket with { SubtitlesDisabled = true }, Connection());
            Assert.True(disabled.SubtitlesDisabled);
            Assert.Null(disabled.SubtitleStreamIndex);
        });
        TestHarness.Test("R-057 无效显式索引不绕过仅强制，实际信息缺失仍关闭", () =>
        {
            var request = Planner(Settings(SubtitleMode.ForcedOnly)).Plan(Ticket(Source()) with { SubtitleStreamIndex = 99 }, Connection());
            Assert.True(request.SubtitlesDisabled);
            Assert.Null(request.SubtitleStreamIndex);
        });
        foreach (var mode in new[] { SubtitleMode.Always, SubtitleMode.ForeignAudioOnly, SubtitleMode.Off })
            TestHarness.Test($"R-057 缺字幕信息时其它模式{mode}保持原语义", () =>
            {
                var settings = Settings(mode);
                var source = Source();
                var request = Plan(settings, source);
                Assert.Equal(mode == SubtitleMode.Off, request.SubtitlesDisabled);
                var sid = InlineSwitch.PerFile(request).First(pair => pair.Key == "sid").Value;
                Assert.Equal(mode == SubtitleMode.Off ? "no" : "auto", sid);
            });
        foreach (var pipeline in new[] { VideoPipelineKind.Integrated, VideoPipelineKind.Standalone })
            TestHarness.Test($"R-057 真服务交假后端的{pipeline}票保留未知仅强制关闭", () =>
                FakeBackendAsync(pipeline, swap: false).GetAwaiter().GetResult());
        TestHarness.Test("R-057 真服务同假句柄换片，从已知普通字幕切未知仅强制关闭", () =>
            FakeBackendAsync(VideoPipelineKind.Standalone, swap: true).GetAwaiter().GetResult());
    }

    private static AppSettings Settings(SubtitleMode mode)
    {
        var settings = new AppSettings();
        settings.Playback.SubtitleMode = mode;
        settings.Playback.SubtitleLanguages = ["中文"];
        settings.Playback.ReportProgressToServer = false;
        settings.Playback.StopReportEnabled = false;
        settings.Playback.ProgressReportIntervalSeconds = 60;
        settings.Playback.SubtitleAssOverride = "";
        settings.Shaders.Enabled = false;
        return settings;
    }
    private static PlaybackPlanner Planner(AppSettings settings) => new(settings, new ShaderGroupResolver(settings.Shaders));
    private static PlaybackRequest Plan(AppSettings settings, MediaSource source) => Planner(settings).Plan(Ticket(source), Connection());
    private static PlaybackTicket Ticket(MediaSource source) => new()
    {
        Item = new EmbyItem { Id = "fixture", Name = "fixture", Type = EmbyItemType.Movie },
        Source = source
    };
    private static MediaSource Source(params MediaStream[] streams) => new() { Id = "source", Container = "mkv", MediaStreams = streams.ToList() };
    private static MediaStream Audio(string language) => new() { Index = 1, Type = "Audio", Language = language, Codec = "aac" };
    private static MediaStream Subtitle(int index, bool forced = false) => new()
    {
        Index = index,
        Type = "Subtitle",
        Language = "chi",
        Codec = "srt",
        IsForced = forced
    };
    private static EmbyConnection Connection() => new(new Uri("https://fixture.invalid/emby/"),
        "placeholder-only", "fixture", "fixture", "fixture", new DeviceIdentity("fixture", "fixture", "fixture", "0"));

    private static async Task FakeBackendAsync(VideoPipelineKind pipeline, bool swap)
    {
        var settings = Settings(SubtitleMode.ForcedOnly);
        settings.Mpv.Pipeline = pipeline;
        var protector = new NoSecrets();
        var transport = new RejectNetwork();
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "forbidden-fixture-config|no-IO"));
        using var session = new EmbySession(settings, new SettingsStore(paths, protector), new CredentialVault(protector),
            Connection().Device, transport);
        // Install fixture state only. No sign-in/restore endpoints, saved secrets or settings are touched.
        var server = new ServerProfile { Id = "fixture-server", Name = "fixture", Url = "https://fixture.invalid" };
        var account = new AccountProfile { Id = "fixture-account", Username = "fixture" };
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var identity = new EmbySessionScope.Identity(new EmbyClient(http, Connection()), server, account);
        var scope = new EmbySessionScope(session, identity);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        sources.Add(scope.Key, scope);
        typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, scope);
        var handle = new PlaybackLifecycleTests.Handle { Swappable = swap, HasControlChannel = true };
        var backend = new PlaybackLifecycleTests.Backend { Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle) };
        var service = new PlaybackService(session, settings, () => backend, Planner(settings));
        var replacementReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.NowPlayingChanged += item =>
        {
            if (item?.Id == "fixture-next") replacementReady.TrySetResult();
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstSource = swap ? Source(Subtitle(2)) : Source();
        var first = Ticket(firstSource) with { SubtitleStreamIndex = swap ? 2 : null };
        var pending = service.PlayAsync(first, scope, deadline.Token);
        await handle.Started.Task.WaitAsync(deadline.Token);
        Task<PlaybackResult>? replacement = null;
        try
        {
            if (swap)
            {
                Assert.False(backend.Starts.Single().SubtitlesDisabled);
                var next = Ticket(Source()) with { Item = new EmbyItem { Id = "fixture-next", Name = "fixture-next", Type = EmbyItemType.Movie } };
                replacement = service.PlayAsync(next, scope, deadline.Token);
                await handle.Swapped.Task.WaitAsync(deadline.Token);
                await replacementReady.Task.WaitAsync(deadline.Token);
                Assert.Equal(1, backend.Starts.Count, "同句柄交接不得重起假后端");
                var active = (object)typeof(PlaybackService).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
                var request = (PlaybackRequest)active.GetType().GetProperty("Request")!.GetValue(active)!;
                Assert.True(request.SubtitlesDisabled);
                Assert.Equal("no", InlineSwitch.PerFile(request).First(pair => pair.Key == "sid").Value);
            }
            else
            {
                var request = backend.Starts.Single();
                Assert.True(request.SubtitlesDisabled);
                Assert.Null(request.SubtitleId);
                Assert.Null(request.SubtitleStreamIndex);
            }
        }
        finally
        {
            handle.End();
            await pending.WaitAsync(deadline.Token);
            if (replacement is not null) await replacement.WaitAsync(deadline.Token);
        }
        Assert.Equal(0, transport.Calls, "测试必须零HTTP，即使播放上报收尾路径也不联网");
        Assert.False(service.IsPlaying);
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("不允许任何凭据/配置 IO");
        public string Unprotect(string cipherText) => throw new AssertionException("不允许任何凭据/配置 IO");
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new AssertionException("不允许测试发送HTTP请求");
        }
    }
}
