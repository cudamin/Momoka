using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlexMultipartTests
{
    public static void Register()
    {
        Test("Plex 分段：同一 Media 只提供一个完整版本", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var item = fixture.Item();
            Assert.Equal(1, item.MediaSources.Count);
            Assert.Equal(7200 * TimeSpan.TicksPerSecond, item.MediaSources.Single().RunTimeTicks);
        });
        Test("Plex 分段：无分段时长不退化成首段版本", () =>
        {
            var media = TwoParts();
            ((JsonObject)((JsonArray)media["Part"]!)[1]!).Remove("duration");
            using var fixture = new Fixture(media);
            Assert.Equal(0, fixture.Item().MediaSources.Count);
        });
        Test("Plex 分段：75 分钟续播保留整片时钟并定位第二段 900 秒", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var item = fixture.Item();
            var request = fixture.Plan(item, 75 * TimeSpan.TicksPerMinute);
            Assert.Equal(4500d, request.StartSeconds);
            Assert.Equal(2, request.MediaParts.Count);
            Assert.Equal("/library/parts/21/1/file.mkv", request.MediaParts[1].MediaUrl.AbsolutePath);
            Assert.Equal("/library/metadata/10", request.PlexSelection!.MetadataKey);
            Assert.Equal(900d, request.StartSeconds - request.MediaParts[0].DurationTicks / (double)TimeSpan.TicksPerSecond);
            Assert.Equal(7200 * TimeSpan.TicksPerSecond, request.RunTimeTicks);
            Assert.Equal(300L, item.MediaSources[0].Size);
            Assert.Contains("layout=this", MpvMediaResource.Build(request));
        });
        Test("Plex 分段：整片时间上报使用条目 metadata key", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var request = fixture.Plan(fixture.Item());
            fixture.Report(new PlaybackReport
            {
                ItemId = "10",
                PlaySessionId = "anonymous",
                PositionTicks = 4500 * TimeSpan.TicksPerSecond,
                PlexSelection = request.PlexSelection
            });
            var uri = fixture.Requests.Last();
            Assert.Contains("key=%2Flibrary%2Fmetadata%2F10", uri.Query);
            Assert.Contains("time=4500000", uri.Query);
            Assert.Contains("duration=7200000", uri.Query);
        });
        Test("Plex 分段：独立 Media 版本不能混串", () =>
        {
            var second = TwoParts();
            second["id"] = "200";
            ((JsonObject)((JsonArray)second["Part"]!)[0]!)["key"] = "/library/parts/30/1/file.mkv";
            ((JsonObject)((JsonArray)second["Part"]!)[1]!)["key"] = "/library/parts/31/1/file.mkv";
            using var fixture = new Fixture(TwoParts(), second);
            var item = fixture.Item();
            Assert.Equal("media:100,media:200", string.Join(',', item.MediaSources.Select(source => source.Id)));
            Assert.Equal(2, item.MediaSources[0].MediaParts.Count);
            Assert.Equal("/library/parts/30/1/file.mkv", item.MediaSources[1].MediaParts[0].MediaUrl.AbsolutePath);
        });
        Test("Plex 分段：无效版本不隐藏另一可用版本", () =>
        {
            var invalid = TwoParts();
            ((JsonObject)((JsonArray)invalid["Part"]!)[1]!)["duration"] = -1;
            using var fixture = new Fixture(invalid, TwoParts());
            Assert.Equal(1, fixture.Item().MediaSources.Count);
        });
        foreach (var duration in new[] { "0", "-1", "NaN", "Infinity", "1e300", "922337203685477.6", "0.00000001" })
        {
            Test("Plex 分段：拒绝非法时长 " + duration, () =>
            {
                var media = TwoParts();
                ((JsonObject)((JsonArray)media["Part"]!)[1]!)["duration"] = duration;
                using var fixture = new Fixture(media);
                Assert.Equal(0, fixture.Item().MediaSources.Count);
            });
        }
        Test("Plex 分段：总时长溢出不能回绕", () =>
        {
            var media = TwoParts();
            foreach (var part in ((JsonArray)media["Part"]!).OfType<JsonObject>()) part["duration"] = 8e14;
            using var fixture = new Fixture(media);
            Assert.Equal(0, fixture.Item().MediaSources.Count);
        });
        Test("Plex 分段：缺失任一 Part 地址不能播放残片", () =>
        {
            var media = TwoParts();
            ((JsonObject)((JsonArray)media["Part"]!)[1]!)["key"] = "";
            using var fixture = new Fixture(media);
            Assert.Equal(0, fixture.Item().MediaSources.Count);
        });
        Test("Plex 分段：所有 Part 都通过原始地址安全边界", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var item = fixture.Item();
            item.MediaSources[0].MediaParts =
            [item.MediaSources[0].MediaParts[0], new PlaybackPart(new Uri("https://external.invalid/second"), 3600 * TimeSpan.TicksPerSecond)];
            Assert.Throws<InvalidOperationException>(() => fixture.Plan(item));
        });
        Test("Plex 分段：mpv EDL 每段独立映射且字节转义保留分隔符", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var request = fixture.Plan(fixture.Item());
            var mapped = new List<Uri>();
            var resource = MpvMediaResource.Build(request, uri =>
            {
                mapped.Add(uri);
                return "http://127.0.0.1:1/分段;,=" + mapped.Count;
            });
            Assert.Equal(2, mapped.Count);
            const string first = "http://127.0.0.1:1/分段;,=1";
            Assert.True(resource.StartsWith("edl://%" + System.Text.Encoding.UTF8.GetByteCount(first) + "%" + first,
                StringComparison.Ordinal));
            Assert.Contains(",length=3600,layout=this;", resource);
            Assert.Contains("分段;,=2,length=3600", resource);
        });
        Test("Plex 分段：非法资源映射拒绝控制字符与空值", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var request = fixture.Plan(fixture.Item());
            Assert.Throws<InvalidOperationException>(() => MpvMediaResource.Build(request, _ => ""));
            Assert.Throws<InvalidOperationException>(() => MpvMediaResource.Build(request, _ => "file\nInjected"));
        });
        Test("Plex 分段：只有一个 Part 保持普通资源和外挂字幕", () =>
        {
            var media = TwoParts();
            ((JsonArray)media["Part"]!).RemoveAt(1);
            AddTracks(media, external: true);
            using var fixture = new Fixture(media);
            var item = fixture.Item();
            var request = fixture.Plan(item);
            Assert.Equal("20", item.MediaSources.Single().Id);
            Assert.Equal(0, request.MediaParts.Count);
            Assert.Equal(request.MediaUrl.AbsoluteUri, MpvMediaResource.Build(request));
            Assert.Equal(1, request.ExternalSubtitles.Count);
        });
        Test("Plex 分段：同一布局保留内置轨并隐藏分段外挂字幕", () =>
        {
            var media = TwoParts();
            AddTracks(media, external: true);
            using var fixture = new Fixture(media);
            var item = fixture.Item();
            var source = item.MediaSources.Single();
            Assert.Equal(3, source.MediaStreams.Count);
            Assert.False(source.MediaStreams.Any(stream => stream.IsExternal));
            Assert.Equal(3, source.DefaultSubtitleStreamIndex);
            Assert.Equal(0, fixture.Plan(item).ExternalSubtitles.Count);
        });
        Test("Plex 分段：内置轨道重排拒绝整个版本", () =>
        {
            var media = TwoParts();
            AddTracks(media, external: false);
            ((JsonObject)((JsonArray)((JsonObject)((JsonArray)media["Part"]!)[1]!)["Stream"]!)[1]!)["language"] = "eng";
            using var fixture = new Fixture(media);
            Assert.Equal(0, fixture.Item().MediaSources.Count);
        });
        Test("Plex 分段：旧外挂字幕选择不静默回退到其它字幕", () =>
        {
            var media = TwoParts();
            AddTracks(media, external: true);
            using var fixture = new Fixture(media);
            var item = fixture.Item();
            var planner = new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders));
            Assert.Throws<NotSupportedException>(() => planner.Plan(new PlaybackTicket
            { Item = item, Source = item.MediaSources[0], SubtitleStreamIndex = 4 }, fixture.Connection));
            var disabled = planner.Plan(new PlaybackTicket
            { Item = item, Source = item.MediaSources[0], SubtitleStreamIndex = 4, SubtitlesDisabled = true }, fixture.Connection);
            Assert.True(disabled.SubtitlesDisabled);
        });
        Test("Plex 分段：Media ID 与另一单文件 Part ID 相同仍是不同版本", () =>
        {
            var single = TwoParts();
            ((JsonArray)single["Part"]!).RemoveAt(1);
            ((JsonObject)((JsonArray)single["Part"]!)[0]!)["id"] = "100";
            using var fixture = new Fixture(single, TwoParts());
            var item = fixture.Item();
            var sources = item.MediaSources;
            Assert.Equal("100", sources[0].Id);
            Assert.Equal("media:100", sources[1].Id);
            Assert.True(MediaVersionSwitch.ShouldSwitch(item, sources[1], sources[0]));
            Assert.True(ReferenceEquals(sources[1], MediaVersionSwitch.Playing(item, new MediaSource { Id = "media:100" })));
        });
        Test("Plex 分段：缺 Media ID 时使用首 Part 标识且不碰单文件版本", () =>
        {
            var media = TwoParts();
            media.Remove("id");
            using var fixture = new Fixture(media);
            Assert.Equal("media:part:20", fixture.Item().MediaSources.Single().Id);
        });
        Test("Plex 分段：中途退出不标已看，整片 EOF 才补满时长", () =>
        {
            using var fixture = new Fixture(TwoParts());
            var item = fixture.Item();
            var request = fixture.Plan(item);
            var planner = new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders));
            var service = new PlaybackService(null!, fixture.Settings,
                () => throw new InvalidOperationException("No real playback permitted"), planner);
            var finalPosition = typeof(PlaybackService).GetMethod("ResolveFinalPosition", BindingFlags.Static | BindingFlags.NonPublic)!;
            var markWatched = typeof(PlaybackService).GetMethod("ShouldMarkWatched", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var ticket = new PlaybackTicket { Item = item, Source = item.MediaSources[0] };
            var halfway = new PlaybackExit(PlaybackEndReason.UserQuit, 3600, 0, null);
            var halfwayTicks = (long)finalPosition.Invoke(null, [halfway, request, ticket])!;
            Assert.Equal(3600 * TimeSpan.TicksPerSecond, halfwayTicks);
            Assert.False((bool)markWatched.Invoke(service, [halfway, request, halfwayTicks])!);
            var eof = new PlaybackExit(PlaybackEndReason.EndOfFile, 7200, 0, null);
            var eofTicks = (long)finalPosition.Invoke(null, [eof, request, ticket])!;
            Assert.Equal(7200 * TimeSpan.TicksPerSecond, eofTicks);
            Assert.True((bool)markWatched.Invoke(service, [eof, request, eofTicks])!);
        });
    }

    private static void AddTracks(JsonObject media, bool external)
    {
        foreach (var part in ((JsonArray)media["Part"]!).OfType<JsonObject>())
        {
            var streams = (JsonArray)JsonNode.Parse("""
                [{"id":1,"streamType":1,"codec":"h264"},
                 {"id":2,"streamType":2,"codec":"aac","language":"chi","default":1},
                 {"id":3,"streamType":3,"codec":"srt","language":"chi","default":1}]
                """)!;
            if (external) streams.Add(new JsonObject
            { ["id"] = 4, ["streamType"] = 3, ["codec"] = "srt", ["key"] = "/library/streams/4" });
            part["Stream"] = streams;
        }
    }

    private static JsonObject TwoParts() => (JsonObject)JsonNode.Parse("""
        {"id":"100","duration":7200000,"Part":[
          {"id":"20","duration":3600000,"key":"/library/parts/20/1/file.mkv","file":"/media/movie-part1.mkv","container":"mkv","size":100},
          {"id":"21","duration":3600000,"key":"/library/parts/21/1/file.mkv","file":"/media/movie-part2.mkv","container":"mkv","size":200}
        ]}
        """)!;

    private sealed class Fixture : IDisposable
    {
        private readonly PlexHttp _http;
        private readonly PlexMediaAdapter _adapter;
        public List<Uri> Requests { get; } = [];
        public AppSettings Settings { get; } = new();
        public EmbyConnection Connection { get; } = new(new Uri("http://plex.invalid:32400/"),
            "placeholder", "7", "offline", "offline", new DeviceIdentity("Momoka", "offline", "device", "1"))
        { ServerKind = MediaServerKind.Plex };

        public Fixture(params JsonObject[] media)
        {
            Settings.Shaders.Enabled = false;
            Settings.Playback.ResumeRewindSeconds = 0;
            _http = new PlexHttp(new Transport(media, Requests));
            _adapter = new PlexMediaAdapter(new PlexClient(_http), new PlexConnection(Connection.ApiBase,
                Connection.AccessToken, "7", "offline", "device", "1"));
        }

        public EmbyItem Item() => _adapter.GetItemAsync("10", default).GetAwaiter().GetResult();
        public void Report(PlaybackReport report) => _adapter.ReportPlaybackProgressAsync(report, default).GetAwaiter().GetResult();

        public PlaybackRequest Plan(EmbyItem item, long start = 0) => new PlaybackPlanner(Settings,
            new ShaderGroupResolver(Settings.Shaders)).Plan(new PlaybackTicket
            { Item = item, Source = item.MediaSources[0], StartTicks = start }, Connection);

        public void Dispose() => _http.Dispose();
    }

    private sealed class Transport(JsonObject[] media, List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri?.Host != "plex.invalid")
                throw new InvalidOperationException("Unexpected anonymous fixture request");
            requests.Add(request.RequestUri);
            if (request.RequestUri.AbsolutePath == "/:/timeline")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            if (request.RequestUri.AbsolutePath != "/library/metadata/10")
                throw new InvalidOperationException("Unexpected anonymous fixture request");
            var item = new JsonObject
            {
                ["ratingKey"] = "10",
                ["type"] = "movie",
                ["title"] = "Two-Part Offline Movie",
                ["duration"] = 7200000,
                ["Media"] = new JsonArray(media.Select(value => value.DeepClone()).ToArray())
            };
            var container = new JsonObject { ["MediaContainer"] = new JsonObject { ["Metadata"] = new JsonArray(item) } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(container.ToJsonString()) });
        }
    }
}
