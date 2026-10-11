using System.Text.Json;
using System.Text.Json.Nodes;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「识别」那几条判据：哪几种条目能识别、输入框写什么字、什么时候压根不该问服务器，以及搜索体的形状。
/// <para>
/// 值得钉住的是那几件「错了屏上也看着正常」的事：给一季摆一条「识别」（服务器不认 <c>Season</c>，点下去
/// 只有 404，而屏上只是「点了没反应」）、一个条件都没填就发一趟空查询（刮削源回一屏不相干的东西，用户却
/// 以为自己填过）、年份里写了「2026年」被原样当成数字发过去（换来一趟没有结果的请求）。
/// </para>
/// </summary>
internal static class ItemIdentifyTests
{
    public static void Register()
    {
        Test("识别：只有服务器查得动的那几种才算支持", () =>
        {
            foreach (var type in (string[])[
                EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.BoxSet, EmbyItemType.Trailer])
                Assert.True(ItemIdentify.Supports(new EmbyItem { Id = "x", Type = type }), $"{type} 应该能识别");

            // 季和单集跟着所属的剧一起识别；文件夹、播放列表、媒体库、演职人员都没有可对上的片子。
            foreach (var type in (string[])[
                EmbyItemType.Season, EmbyItemType.Episode, EmbyItemType.Folder, EmbyItemType.Playlist,
                EmbyItemType.CollectionFolder, EmbyItemType.Person, ""])
                Assert.False(ItemIdentify.Supports(new EmbyItem { Id = "x", Type = type }), $"{type} 不该有识别");
        });

        Test("识别：输入框标签是「名字 + 标识符」", () =>
        {
            // 和网页端同一句（LabelDynamicExternalId）：用户截图里那三格就是「IMDb 标识符」这样的字。
            Assert.Equal("IMDb 标识符", ItemIdentify.FieldLabel("IMDb"));
            Assert.Equal("TheMovieDb 标识符", ItemIdentify.FieldLabel("TheMovieDb"));
        });

        Test("识别：一个条件都没填就别问", () =>
        {
            Assert.False(ItemIdentify.HasCriteria(null, [null, "  "]));
            Assert.False(ItemIdentify.HasCriteria("   ", []));

            Assert.True(ItemIdentify.HasCriteria("柯蒂斯总统", []));
            Assert.True(ItemIdentify.HasCriteria(null, ["tt123"]), "只填标识符也算条件");
        });

        Test("识别：年份只认得出整数的才算填了", () =>
        {
            Assert.Equal((int?)2026, ItemIdentify.Year(" 2026 "));
            Assert.Equal((int?)1900, ItemIdentify.Year("1900"));

            // 交一句刮削源解析不了的话过去，换来的是一趟没有结果的请求 —— 当作没填比猜一个数字好。
            Assert.Null(ItemIdentify.Year("2026年"));
            Assert.Null(ItemIdentify.Year("unknown"));
            Assert.Null(ItemIdentify.Year(""));
            Assert.Null(ItemIdentify.Year(null));
            Assert.Null(ItemIdentify.Year("0"));
        });

        Test("识别：搜索体里空的那几位不发，条目 id 一定带上", () =>
        {
            var item = new EmbyItem { Id = "m1", Name = "某片", Type = EmbyItemType.Movie };

            var query = ItemIdentify.SearchQuery(
                item, "  柯蒂斯总统  ", "2026", [("Imdb", "tt123"), ("Tmdb", "   "), ("Tvdb", "999")]);

            var body = JsonNode.Parse(JsonSerializer.Serialize(query, EmbyHttp.Json))!;

            Assert.Equal("柯蒂斯总统", body["SearchInfo"]!["Name"]!.GetValue<string>(), "首尾空白去掉");
            Assert.Equal((int?)2026, (int?)body["SearchInfo"]!["Year"]);
            Assert.Equal("tt123", body["SearchInfo"]!["ProviderIds"]!["Imdb"]!.GetValue<string>());
            Assert.Equal("999", body["SearchInfo"]!["ProviderIds"]!["Tvdb"]!.GetValue<string>());
            Assert.Null(body["SearchInfo"]!["ProviderIds"]!["Tmdb"], "留空的那一家不发");
            Assert.Equal("m1", body["ItemId"]!.GetValue<string>());
        });

        Test("识别：名字和年份都空着时那两位不出现（服务器自己区分「没填」和「填了空串」）", () =>
        {
            var item = new EmbyItem { Id = "s1", Name = "某剧", Type = EmbyItemType.Series };

            var body = JsonNode.Parse(JsonSerializer.Serialize(
                ItemIdentify.SearchQuery(item, "  ", "", [("Tvdb", "12345")]), EmbyHttp.Json))!;

            Assert.Null(body["SearchInfo"]!["Name"]);
            Assert.Null(body["SearchInfo"]!["Year"]);
            Assert.Equal("12345", body["SearchInfo"]!["ProviderIds"]!["Tvdb"]!.GetValue<string>());
        });

        Test("识别：服务器发来的那一条解出来再发回去，没点名的字段一个不少", () =>
        {
            // 提交要把原样那一条交回去。逐字段抄一遍等于承诺「服务器的字段表我全知道」，而那张表跟着版本走。
            const string payload = """
                {"Name":"柯蒂斯总统","ProviderIds":{"Tmdb":"296756"},"ProductionYear":2026,
                 "SearchProviderName":"TheMovieDb","ImageUrl":"https://image.tmdb.org/x.jpg",
                 "SomeFutureField":{"a":1},"SortIndexNumber":7}
                """;

            var result = JsonSerializer.Deserialize<RemoteSearchResult>(payload, EmbyHttp.Json)!;
            var sent = JsonSerializer.Serialize(result, EmbyHttp.Json);

            Assert.Equal("柯蒂斯总统", result.Name);
            Assert.Equal("TheMovieDb", result.SearchProviderName);
            Assert.Equal("296756", result.ProviderIds["Tmdb"]);
            Assert.Contains("SomeFutureField", sent);
            Assert.Contains("SortIndexNumber", sent);
        });

        Test("识别：候选那一行小字是哪一家给的、哪一年", () =>
        {
            Assert.Equal("TheMovieDb  ·  2026", new RemoteSearchResult
            {
                SearchProviderName = "TheMovieDb",
                ProductionYear = 2026
            }.Describe());

            // 一家都没说、也没有年份时是空串，不是一串分隔符。
            Assert.Equal("", new RemoteSearchResult().Describe());
        });
    }
}
