using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasMediaTests
{
    private static void RegisterVersionDurations()
    {
        (string Item, string Progress, string Version, long? SourceSeconds, long DisplaySeconds)[] cases =
        [
            ("missing", "missing", "missing", null, 120),
            ("0", "missing", "missing", null, 120),
            ("null", "null", "null", null, 120),
            ("-1", "-2", "-3", null, 120),
            ("\"NaN\"", "\"Infinity\"", "\"NaN\"", null, 120),
            ("\"Infinity\"", "\"NaN\"", "\"Infinity\"", null, 120),
            ("0", "0", "0", null, 120),
            ("0", "missing", "240", 240, 120),
            ("90", "missing", "missing", 90, 90),
            ("90", "0", "missing", 90, 90),
            ("0", "90", "missing", 90, 90),
            ("90", "180", "missing", 180, 180),
            ("90", "missing", "240", 240, 90),
            ("0", "90", "240", 240, 90)
        ];
        foreach (var (itemDuration, progressDuration, versionDuration, sourceSeconds, displaySeconds) in cases)
            Test($"飞牛版本时长：条目{itemDuration}/进度{progressDuration}/版本{versionDuration}不借其它版", () =>
            {
                using var fixture = new Fixture();
                fixture.Login();
                var item = ReadVersionDurationItem(fixture, itemDuration, progressDuration, versionDuration);
                Assert.Equal(displaySeconds * TimeSpan.TicksPerSecond, item.RunTimeTicks);
                Assert.Equal(12 * TimeSpan.TicksPerSecond, item.ResumeTicks);
                Assert.Equal(120 * TimeSpan.TicksPerSecond, item.MediaSources[0].RunTimeTicks);
                Assert.Equal(240 * TimeSpan.TicksPerSecond, item.MediaSources[2].RunTimeTicks);
                var source = item.MediaSources[1];
                long? expectedTicks = sourceSeconds * TimeSpan.TicksPerSecond;
                Assert.Equal(expectedTicks, source.RunTimeTicks);
                AssertVersionDuration(fixture, item, source, (sourceSeconds ?? 0) * TimeSpan.TicksPerSecond);
            });

        Test("飞牛版本时长：直接票据的未知源不使用详情展示时长", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var item = ReadVersionDurationItem(fixture, "0", "missing", "missing");
            item.MediaSources[1].RunTimeTicks = null;
            item.RunTimeTicks = 120 * TimeSpan.TicksPerSecond;
            AssertVersionDuration(fixture, item, item.MediaSources[1], 0);
        });
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.Plex })
            Test($"飞牛版本时长：{kind} 的既有条目时长回退保留", () =>
            {
                using var fixture = new Fixture();
                fixture.Login();
                var item = ReadVersionDurationItem(fixture, "90", "missing", "missing");
                var source = item.MediaSources[1];
                source.RunTimeTicks = null;
                source.Path = "/library/parts/file-b/file.mkv";
                var planner = new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders));
                var request = planner.Plan(new PlaybackTicket { Item = item, Source = source },
                    fixture.Session.Connection! with { ServerKind = kind });
                Assert.Equal(90 * TimeSpan.TicksPerSecond, request.RunTimeTicks);
                Assert.Null(request.FnNasSelection);
                if (kind == MediaServerKind.Plex) Assert.Equal(90 * TimeSpan.TicksPerSecond, request.PlexSelection!.DurationTicks);
            });
    }

    private static void AssertVersionDuration(Fixture fixture, EmbyItem item, MediaSource source, long expectedTicks)
    {
        var planner = new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders));
        var request = planner.Plan(new PlaybackTicket { Item = item, Source = source, StartTicks = item.ResumeTicks }, fixture.Session.Connection!);
        Assert.Equal(expectedTicks, request.RunTimeTicks);
        Assert.Equal(expectedTicks, request.FnNasSelection!.DurationTicks);
        var record = FnNasMediaAdapter.PlaybackRecord(new PlaybackReport
        {
            ItemId = item.Id,
            MediaSourceId = source.Id,
            PlaySessionId = "fixture",
            PositionTicks = item.ResumeTicks,
            FnNasSelection = request.FnNasSelection
        });
        Assert.Equal(expectedTicks / TimeSpan.TicksPerSecond, record["duration"]!.GetValue<long>());
        Assert.Equal(12L, record["ts"]!.GetValue<long>());
    }

    private static EmbyItem ReadVersionDurationItem(Fixture fixture, string itemDuration, string progressDuration, string versionDuration)
    {
        var item = new JsonObject { ["guid"] = "movie", ["title"] = "匿名多版本", ["type"] = "Movie" };
        var progress = new JsonObject { ["ts"] = 12 };
        var unknownFile = new JsonObject { ["guid"] = "file-b", ["path"] = "/media/b.mkv" };
        if (itemDuration != "missing") item["duration"] = JsonNode.Parse(itemDuration);
        if (progressDuration != "missing") progress["duration"] = JsonNode.Parse(progressDuration);
        if (versionDuration != "missing") unknownFile["duration"] = JsonNode.Parse(versionDuration);
        var streams = new JsonObject
        {
            ["files"] = new JsonArray(
                new JsonObject { ["guid"] = "file-a", ["path"] = "/media/a.mkv", ["duration"] = 120 },
                unknownFile,
                new JsonObject { ["guid"] = "file-c", ["path"] = "/media/c.mkv", ["duration"] = 240 }),
            ["video_streams"] = new JsonArray(
                new JsonObject { ["guid"] = "video-a", ["media_guid"] = "file-a", ["index"] = 0, ["codec_name"] = "h264" },
                new JsonObject { ["guid"] = "video-b", ["media_guid"] = "file-b", ["index"] = 0, ["codec_name"] = "h264" },
                new JsonObject { ["guid"] = "video-c", ["media_guid"] = "file-c", ["index"] = 0, ["codec_name"] = "h264" }),
            ["audio_streams"] = new JsonArray()
        };
        fixture.Transport.Override = (request, _) =>
        {
            var data = request.RequestUri!.AbsolutePath switch
            {
                "/v/api/v1/item/movie" => item,
                "/v/api/v1/play/info" => progress,
                "/v/api/v1/stream/list/movie" => streams,
                _ => null
            };
            return data is null ? null : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"code\":0,\"data\":" + data.ToJsonString() + "}") });
        };
        return fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
    }
}
