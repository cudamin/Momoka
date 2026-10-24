using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotMediaDetailTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static MoviePilotMedia Media => new() { Title = "示例剧", Type = "电视剧", MediaSource = "themoviedb", MediaId = "42" };
    private static MoviePilotSubscription Subscription(int id, int season, string group, int mode = 0, string source = "themoviedb") =>
        MoviePilotSubscription.Parse(JsonSerializer.SerializeToElement(new
        {
            id,
            name = "示例剧",
            type = "电视剧",
            media_source = source,
            media_id = "42",
            season,
            episode_group = group,
            best_version = mode > 0 ? 1 : 0,
            best_version_full = mode == 2 ? 1 : 0
        }))!;
    private static MoviePilotService Service(StubTransport transport)
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true; settings.MoviePilot.Url = "https://fixture.invalid:3001"; settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture-password");
        transport.Answer("login/access-token", """{"access_token":"fixture-token"}""");
        return new(new MoviePilotClient(transport), new(PassthroughSecretProtector.Instance), settings);
    }

    internal static void Register()
    {
        Test("MP 媒体详情：原始标题国家公司与类型身份匹配", () =>
        {
            var data = Json("""{"title":"示例剧","media_source":"themoviedb","media_id":42,"tmdb_id":42,"type":"电视剧","original_name":"Example","production_countries":[{"name":"日本"}],"production_companies":[{"name":"Studio"}],"poster_path":"/poster.jpg","season_info":[]}""");
            var detail = MoviePilotMediaDetail.Parse(data, Media);
            Assert.True(detail.Facts.Any(item => item.Value == "Example"));
            Assert.True(detail.Facts.Any(item => item.Value == "日本"));
            Assert.Equal("日本", detail.Countries);
            Assert.Equal("", detail.OriginalLanguage);
            Assert.Equal("https://image.tmdb.org/t/p/original/poster.jpg", detail.Media.PosterUrl);
            Assert.Throws<MoviePilotException>(() => MoviePilotMediaDetail.Parse(data, Media with { Type = "电影" }));
            Assert.Throws<MoviePilotException>(() => MoviePilotMediaDetail.Parse(data, Media with { MediaSource = "douban" }));
        });
        Test("MP 季列表：保留组的稀疏季号及特别篇，不按位置重新编号", () =>
        {
            var seasons = MoviePilotMediaSeason.Parse(Json("""[{"season_number":0,"episode_count":2},{"season_number":3,"episode_count":"12"},{"season_number":7,"episode_count":8}]"""));
            Assert.Equal(3, seasons[0].Number); Assert.Equal(7, seasons[1].Number); Assert.Equal(0, seasons[2].Number);
            var episodes = MoviePilotMediaEpisode.Parse(Json("""[{"episode_number":2,"season_number":99,"name":"组内第二集"}]"""));
            Assert.Equal(2, episodes[0].Number);
            Assert.Throws<MoviePilotException>(() => MoviePilotMediaSeason.Parse(Json("{}")));
        });
        Test("MP 季订阅：仅当前组和可见季参与差量，异源异组不被取消", () =>
        {
            var plan = MoviePilotSubscriptionPlan.Build(Media, "group-a", [new(1, false, MoviePilotSubscribeMode.Normal), new(2, true, MoviePilotSubscribeMode.FullSeason)],
                [Subscription(1, 1, "group-a"), Subscription(2, 1, "group-b"), Subscription(3, 3, "group-a"), Subscription(4, 2, "group-a", source: "douban")]);
            Assert.Equal(2, plan.Count); Assert.Equal(1, plan[0].Original!.Id); Assert.Equal(MoviePilotSubscribeChangeKind.Delete, plan[0].Kind);
            Assert.Equal(MoviePilotSubscribeChangeKind.Add, plan[1].Kind); Assert.Equal("group-a", plan[1].EpisodeGroup);
        });
        Test("MP 季订阅：三种模式真实参数与仅模式修改的最小正文", () =>
        {
            foreach (var mode in Enum.GetValues<MoviePilotSubscribeMode>())
            {
                var add = MoviePilotSubscriptionPlan.Build(Media, "group", [new(0, true, mode)], []).Single();
                var body = MoviePilotSubscriptionPlan.Body(add);
                Assert.Equal(0, (int)body["season"]!); Assert.Equal("group", body["episode_group"]);
                Assert.Equal(mode == MoviePilotSubscribeMode.Normal ? 0 : 1, (int)body["best_version"]!);
                Assert.Equal(mode == MoviePilotSubscribeMode.FullSeason ? 1 : 0, (int)body["best_version_full"]!);
            }
            var update = MoviePilotSubscriptionPlan.Build(Media, "group", [new(1, true, MoviePilotSubscribeMode.Episodes)], [Subscription(7, 1, "group")]).Single();
            var patch = MoviePilotSubscriptionPlan.Body(update);
            Assert.Equal(3, patch.Count); Assert.Equal(7, (int)patch["id"]!); Assert.False(patch.ContainsKey("episode_group"));
            Assert.Equal(0, MoviePilotSubscriptionPlan.Build(Media, "group", [new(1, true, MoviePilotSubscribeMode.Normal)], [Subscription(7, 1, "group")]).Count);
        });
        Test("MP 季订阅：电影空季号和非法选择保护", () =>
        {
            var movie = Media with { Type = "电影" };
            var body = MoviePilotSubscriptionPlan.Body(MoviePilotSubscriptionPlan.Build(movie, "", [new(null, true, MoviePilotSubscribeMode.Normal)], []).Single());
            Assert.Null(body["season"]);
            Assert.Throws<MoviePilotException>(() => MoviePilotSubscriptionPlan.Build(Media, "", [new(null, true, MoviePilotSubscribeMode.Normal)], []));
            Assert.Throws<MoviePilotException>(() => MoviePilotSubscriptionPlan.Build(Media, "", [new(1, true, (MoviePilotSubscribeMode)9)], []));
        });
        Test("MP 非 TMDB 季列表：按原来源查询且不猜季号", () =>
        {
            using var transport = new StubTransport().Answer("media/seasons", """[{"season_number":4,"episode_count":12},{"episode_count":99}]""");
            using var service = Service(transport);
            var media = Media with { MediaSource = "douban", ConnectionStamp = service.ConnectionStamp };
            var detail = new MoviePilotMediaDetail(media, Json("{}"));
            var seasons = service.MediaSeasonsAsync(detail, "", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(4, seasons.Single().Number);
            Assert.Contains("media_source=douban&media_id=42", transport.Only("media/seasons").Url);
            Assert.Throws<MoviePilotException>(() => service.MediaSeasonsAsync(detail, "tmdb-group", CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("MP 组集读取：组参数属于 Query，响应原始季号不覆盖请求季号", () =>
        {
            using var transport = new StubTransport().Answer("tmdb/42/3", """[{"episode_number":1,"season_number":99,"name":"第一集"}]""");
            using var service = Service(transport);
            var media = Media with { ConnectionStamp = service.ConnectionStamp };
            var detail = new MoviePilotMediaDetail(media, Json("{}"));
            var episodes = service.MediaEpisodesAsync(detail, "group a", 3, CancellationToken.None).GetAwaiter().GetResult();
            var request = new Uri(transport.Only("tmdb/42/3").Url);
            Assert.Equal("/api/v1/tmdb/42/3", request.AbsolutePath); Assert.Equal("?episode_group=group%20a", request.Query);
            Assert.Equal(1, episodes.Single().Number);
        });
        Test("MP 新增季：读回明确 ID 核实真实模式，不能将重复 POST 当模式已改变", () =>
        {
            using var transport = new StubTransport().Answer("subscribe/9", Subscription(9, 1, "", 0).Data.GetRawText())
                .Sequence("subscribe/", (HttpStatusCode.OK, "[]"), (HttpStatusCode.OK, """{"success":true,"data":{"id":9}}"""));
            using var service = Service(transport);
            var media = Media with { ConnectionStamp = service.ConnectionStamp };
            var add = MoviePilotSubscriptionPlan.Build(media, "", [new(1, true, MoviePilotSubscribeMode.FullSeason)], []).Single();
            Assert.Throws<MoviePilotException>(() => service.ApplySubscriptionChangeAsync(add, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(MoviePilotOperationState.Uncertain, service.SeasonSubscribeState(add.Media));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.ApplySubscriptionChangeAsync(add, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.SentTo("subscribe/").Count(item => item.Method == "POST"));
            service.ConfirmSeasonSubscriptionReviewed(add.Media);
            Assert.Equal(MoviePilotOperationState.Idle, service.SeasonSubscribeState(add.Media));
        });
        Test("MP 新增季：回执缺编号进入待核对，切换组或模式不能绕过防重", () =>
        {
            using var transport = new StubTransport().Sequence("subscribe/", (HttpStatusCode.OK, "[]"), (HttpStatusCode.OK, """{"success":true,"data":null}"""));
            using var service = Service(transport);
            var media = Media with { ConnectionStamp = service.ConnectionStamp };
            var add = MoviePilotSubscriptionPlan.Build(media, "a", [new(0, true, MoviePilotSubscribeMode.Normal)], []).Single();
            Assert.Throws<MoviePilotException>(() => service.ApplySubscriptionChangeAsync(add, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.ApplySubscriptionChangeAsync(add with { EpisodeGroup = "b", Mode = MoviePilotSubscribeMode.FullSeason }, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.SentTo("subscribe/").Count(item => item.Method == "POST"));
        });
        Test("MP 字幕：单集搜索与原始签名对象下载，回执文件数和重复保护", () =>
        {
            using var transport = new StubTransport()
                .Answer("search/subtitle/media/42", """[{"title":"示例字幕","site":8,"language":"简中","enclosure":"https://fixture.invalid/signed?sig=fixture","site_cookie":"fixture-secret"}]""")
                .Answer("download/subtitle", """{"success":true,"data":{"files":["/字幕/示例.srt"]}}""");
            using var service = Service(transport);
            var media = Media with { ConnectionStamp = service.ConnectionStamp, Season = 0 };
            var subtitle = service.SearchSubtitlesAsync(media, new(service.ConnectionStamp, [8]), 2, CancellationToken.None).GetAwaiter().GetResult().Single();
            var query = transport.Only("search/subtitle/media/42").Url;
            Assert.Contains("season=0", query); Assert.Contains("episode=2", query); Assert.Contains("sites=8", query);
            Assert.Equal(1, service.DownloadSubtitleAsync(subtitle, "/字幕", CancellationToken.None).GetAwaiter().GetResult());
            var body = Json(transport.Only("download/subtitle").Body);
            Assert.True(JsonElement.DeepEquals(subtitle.Data, body.GetProperty("subtitle_in")));
            Assert.Equal("/字幕", body.GetProperty("save_path").GetString());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadSubtitleAsync(subtitle, null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("download/subtitle"));
        });
    }
}
