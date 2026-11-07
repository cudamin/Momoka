using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterBitrates()
    {
        Test("Plex 码率：媒体和轨道从 kbps 转成 bit/s，实际媒体信息使用正确单位", () =>
        {
            var source = BitrateSource(JsonValue.Create(8000), JsonValue.Create("7000"), JsonValue.Create(768));
            Assert.Equal(8_000_000, source.Bitrate);
            Assert.Equal(7_000_000, source.PrimaryVideoStream!.BitRate);
            Assert.Equal(768_000, source.MediaStreams.Single(stream => stream.IsAudio).BitRate);
            var info = ItemDetail.MediaInfo(new EmbyItem { Type = EmbyItemType.Movie }, source);
            Assert.Equal("7 Mb/s", info.Single(row => row.Label == "码 率：").Value);
            Assert.Contains("768 kb/s", string.Join(' ', info.Select(row => row.Value + row.Second)));
        });
        Test("Plex 码率：视频未给码率时使用已经换算的媒体总码率", () =>
        {
            var source = BitrateSource(JsonValue.Create("8000"), null, null);
            var info = ItemDetail.MediaInfo(new EmbyItem { Type = EmbyItemType.Movie }, source);
            Assert.Equal("8 Mb/s", info.Single(row => row.Label == "码 率：").Value);
        });

        // 期望为独立手算值。保留现有 Integer 的截断/有符号语义，只拒绝转换后无法表示的值。
        (string Json, int? Bits)[] cases =
        [
            ("0", 0), ("-1", -1000), ("8000.75", 8_000_000), ("\"8000\"", 8_000_000),
            ("2147483", 2_147_483_000), ("2147484", null), ("2147483647", null),
            ("-2147483", -2_147_483_000), ("-2147484", null), ("-2147483648", null),
            ("null", null), ("\"\"", null), ("\"abc\"", null), ("\"NaN\"", null),
            ("\"Infinity\"", null), ("2147483648", null)
        ];
        foreach (var (json, bits) in cases)
            Test($"Plex 码率：数值边界 {json}", () =>
            {
                var source = BitrateSource(JsonNode.Parse(json), JsonNode.Parse(json), JsonNode.Parse(json));
                Assert.Equal(bits, source.Bitrate);
                Assert.True(source.MediaStreams.All(stream => stream.BitRate == bits));
            });
        Test("Plex 码率：缺字段不补零或展示未知码率", () =>
        {
            var source = BitrateSource(null, null, null, missing: true);
            Assert.Equal<int?>(null, source.Bitrate);
            Assert.True(source.MediaStreams.All(stream => stream.BitRate is null));
            Assert.False(ItemDetail.MediaInfo(new EmbyItem { Type = EmbyItemType.Movie }, source)
                .Any(row => row.Label == "码 率："));
        });
    }

    private static MediaSource BitrateSource(JsonNode? mediaBits, JsonNode? videoBits, JsonNode? audioBits, bool missing = false)
    {
        var video = new JsonObject { ["id"] = 1, ["streamType"] = 1, ["codec"] = "h264" };
        var audio = new JsonObject { ["id"] = 2, ["streamType"] = 2, ["codec"] = "aac" };
        var media = new JsonObject
        {
            ["Part"] = new JsonArray(new JsonObject
            {
                ["id"] = "part", ["key"] = "/library/parts/part/file.mkv",
                ["Stream"] = new JsonArray(video, audio)
            })
        };
        if (!missing)
        {
            media["bitrate"] = mediaBits;
            video["bitrate"] = videoBits;
            audio["bitrate"] = audioBits;
        }
        using var http = new PlexHttp(new Transport());
        return PlexMediaAdapter.Sources(new JsonObject { ["Media"] = new JsonArray(media) }, "item",
            new PlexClient(http), new PlexConnection(new Uri("http://plex.invalid/"), "fixture-token")).Single();
    }
}
