using System.Text.Json.Nodes;
using Momoka.FnNas;

namespace Momoka.Tests;

internal static class FnNasLibraryInputTests
{
    public static void Register()
    {
        TestHarness.Test("R-020 旧直播源判据：新库/缺失源/未知类型都不是已保存播放列表", () =>
        {
            Assert.False(FnNasLibraryInput.HasStoredIptvSource(null));
            Assert.False(FnNasLibraryInput.HasStoredIptvSource(Document("IPTV")));
            foreach (var source in new JsonNode?[] { null, new JsonObject(), new JsonObject { ["source_type"] = "" },
                new JsonObject { ["source_type"] = "future" }, JsonValue.Create("not-object") })
            {
                var original = Document("IPTV");
                original["iptv"] = source;
                Assert.False(FnNasLibraryInput.HasStoredIptvSource(original));
            }
        });
        foreach (var category in new[] { "Movie", "TV", "Mix", "Others" })
            TestHarness.Test($"R-020 旧源判据：{category}残留text字段不证明原库为IPTV", () =>
            {
                var original = Document(category);
                original["iptv"] = new JsonObject { ["source_type"] = "text" };
                Assert.False(FnNasLibraryInput.HasStoredIptvSource(original));
            });
        foreach (var type in new[] { "text", "local_file", "upload" })
            TestHarness.Test($"R-020 明确原IPTV {type}允许只改名保留，不要求服务器回传源内容", () =>
            {
                var original = Document("IPTV");
                original["iptv"] = new JsonObject { ["source_type"] = type };
                var before = original.ToJsonString();
                Assert.True(FnNasLibraryInput.HasStoredIptvSource(original));
                Assert.Equal(before, original.ToJsonString());
            });
        TestHarness.Test("R-020 URL源依旧不是保留文件分支，URL文本必须继续验证", () =>
        {
            var original = Document("IPTV");
            original["iptv"] = new JsonObject { ["source_type"] = "url", ["source_url"] = "https://channel.invalid/list.m3u8", ["refresh_interval"] = 0 };
            Assert.False(FnNasLibraryInput.HasStoredIptvSource(original));
            FnNasLibraryInput.Validate(original, editing: true);
            original["iptv"]!["source_url"] = "";
            Assert.Throws<FnNasApiException>(() => FnNasLibraryInput.Validate(original, editing: true));
        });
        TestHarness.Test("R-020 原IPTV保留源的省略字段契约不改，新建仍不得省略", () =>
        {
            var kept = Document("IPTV");
            FnNasLibraryInput.Validate(kept, editing: true);
            Assert.Throws<FnNasApiException>(() => FnNasLibraryInput.Validate(kept, editing: false));
        });
    }

    private static JsonObject Document(string category) => new() { ["name"] = "fixture", ["category"] = category };
}
