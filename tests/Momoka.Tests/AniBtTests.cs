using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtTests
{
    private const string MatchResponse = """
        {
          "version":3,
          "content":{"kind":"anime_episode","confidence":"high"},
          "structure":{"season":2},
          "decision":{"kind":"auto","confidence":"high","reason":"top_ranked_candidate","animeId":"ani_one","bgmId":123,"vetoes":[]},
          "candidates":[{
            "animeId":"ani_one","bgmId":123,"matchScore":100,
            "anime":{"_id":"ani_one","bgmId":123,"title":{"chinese":"测试动画","native":"Test Anime"},"episodes":12,"seasonYear":2026,"format":"TV"}
          }]
        }
        """;

    public static void Register()
    {
        Test("遗漏集搜索分流：MP 拼季集，AniBT 番剧名与集号分开", () =>
        {
            var owner = new EmbyItem { Name = "第二季", SeriesName = "测试动画", Type = EmbyItemType.Season, IndexNumber = 2 };
            var missing = new EmbyItem { Name = "一个单集标题", Type = EmbyItemType.Episode, IndexNumber = 4, ParentIndexNumber = 2 };
            Assert.Equal("测试动画 S02 E04", MoviePilotVersionQuery.Keyword(missing, owner));
            Assert.Equal(new AniBtSearchQuery("测试动画", 4), AniBtSearchQuery.From(missing, owner));
            Assert.Equal(new AniBtSearchQuery("测试动画", null), AniBtSearchQuery.From(owner));
            missing.ParentIndexNumber = 0;
            missing.IndexNumber = 0;
            Assert.Equal("测试动画 S00 E00", MoviePilotVersionQuery.Keyword(missing, owner));
            Assert.Equal(0, AniBtSearchQuery.From(missing, owner).Episode);
            missing.IndexNumber = null;
            Assert.Null(AniBtSearchQuery.From(missing, owner).Episode);
            Assert.Equal("测试动画 S00", MoviePilotVersionQuery.Keyword(missing, owner));
            Assert.Null(missing.SeriesName);
        });

        Test("AniBT RSS 分集使用独立 episode 并省略默认 limit", () =>
        {
            using var transport = new Transport((_, _) => Xml(Feed(Release("rel_one", "123", "https://anibt.net/api/torrent/rel_one.torrent"))));
            using var client = new AniBtClient(transport);
            var anime = new AniBtAnime("", 123, "测试动画");
            var found = client.ReleasePageAsync(anime, default, 4).GetAwaiter().GetResult();
            Assert.Equal("https://anibt.net/rss/anime.xml?bgmId=123&episode=4", transport.Requests[0].Url);
            Assert.Equal(123L, found.BgmId);
            client.ReleasesAsync(anime, default).GetAwaiter().GetResult();
            Assert.Equal("https://anibt.net/rss/anime.xml?bgmId=123", transport.Requests[1].Url);
            Assert.True(transport.Requests.All(request => request.Method == "GET" && !request.HasCredentials));
        });

        Test("AniBT RSS 编号合并后按新身份校验并保留分集参数", () =>
        {
            using var transport = new Transport((_, index) => index == 1
                ? Redirect("/rss/anime.xml?bgmId=456&episode=4")
                : Xml(Feed(Release("rel_one", "456", "https://anibt.net/api/torrent/rel_one.torrent")
                    + Release("rel_old", "123", "https://anibt.net/api/torrent/rel_old.torrent"))));
            using var client = new AniBtClient(transport);
            var result = client.ReleasePageAsync(new AniBtAnime("", 123, "测试动画"), default, 4).GetAwaiter().GetResult();
            Assert.Equal(456L, result.BgmId);
            Assert.Equal("rel_one", result.Items.Single().ReleaseId);
            Assert.Equal(2, transport.Requests.Count);
        });

        Test("AniBT RSS 跳转不能丢失或改写分集条件", () =>
        {
            foreach (var target in new[] { "/rss/anime.xml?bgmId=456", "/rss/anime.xml?bgmId=456&episode=5" })
            {
                using var transport = new Transport((_, _) => Redirect(target));
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => client.ReleasePageAsync(new AniBtAnime("", 123, "测试动画"), default, 4).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Requests.Count);
            }
        });

        Test("AniBT 识别只发送源文件名并核对自动候选身份", () =>
        {
            using var transport = new Transport((_, _) => Json(MatchResponse));
            using var client = new AniBtClient(transport);
            var result = client.MatchAsync(@"C:\private\library\[Team] Test Anime - #01.mkv", default).GetAwaiter().GetResult();
            Assert.Equal(1, transport.Requests.Count);
            var request = transport.Requests[0];
            Assert.Equal("POST", request.Method);
            Assert.Equal("https://anibt.net/api/animes/torrent-match", request.Url);
            Assert.False(request.HasCredentials);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("[Team] Test Anime - #01.mkv", body.RootElement.GetProperty("rawTitle").GetString());
            Assert.DoesNotContain("private", request.Body);
            Assert.Equal(123L, result.AutomaticCandidate!.BgmId);
            Assert.Equal(2, result.StructureSeason);
        });

        Test("AniBT 手动候选即使满分也不会自动采用", () =>
        {
            using var transport = new Transport((_, _) => Json(MatchResponse.Replace("\"kind\":\"auto\"", "\"kind\":\"manual\"", StringComparison.Ordinal)));
            using var client = new AniBtClient(transport);
            var result = client.MatchAsync("Test Anime.mkv", default).GetAwaiter().GetResult();
            Assert.Equal(1, result.Candidates.Count);
            Assert.Null(result.AutomaticCandidate);
        });

        Test("AniBT 自动决定必须属于返回候选且没有否决条件", () =>
        {
            foreach (var body in new[]
            {
                MatchResponse.Replace("\"reason\":\"top_ranked_candidate\",\"animeId\":\"ani_one\"", "\"reason\":\"top_ranked_candidate\",\"animeId\":\"ani_other\"", StringComparison.Ordinal),
                MatchResponse.Replace("\"vetoes\":[]", "\"vetoes\":[\"conflict\"]", StringComparison.Ordinal),
                MatchResponse.Replace("\"kind\":\"anime_episode\"", "\"kind\":\"multi_season_bundle\"", StringComparison.Ordinal)
            })
            {
                using var transport = new Transport((_, _) => Json(body));
                using var client = new AniBtClient(transport);
                Assert.Null(client.MatchAsync("Test Anime.mkv", default).GetAwaiter().GetResult().AutomaticCandidate);
            }
        });

        Test("AniBT 候选内外编号冲突时不能成为可选媒体", () =>
        {
            using var transport = new Transport((_, _) => Json(MatchResponse.Replace("\"_id\":\"ani_one\",\"bgmId\":123", "\"_id\":\"ani_one\",\"bgmId\":456", StringComparison.Ordinal)));
            using var client = new AniBtClient(transport);
            var result = client.MatchAsync("Test Anime.mkv", default).GetAwaiter().GetResult();
            Assert.Equal(0, result.Candidates.Count);
            Assert.Null(result.AutomaticCandidate);
        });

        Test("AniBT 匹配未知协议版本不沿用自动决定", () =>
        {
            using var transport = new Transport((_, _) => Json(MatchResponse.Replace("\"version\":3", "\"version\":4", StringComparison.Ordinal)));
            using var client = new AniBtClient(transport);
            Assert.Throws<AniBtException>(() => client.MatchAsync("Test Anime.mkv", default).GetAwaiter().GetResult());
        });

        Test("AniBT 拒绝把视频 URL 与访问参数作为源文件名发送", () =>
        {
            using var transport = new Transport((_, _) => Json(MatchResponse));
            using var client = new AniBtClient(transport);
            Assert.Throws<AniBtException>(() => client.MatchAsync("https://example.invalid/video.mkv?token=private", default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests.Count);
        });

        Test("AniBT 搜索转义关键词并保留空值且不伪造翻页", () =>
        {
            using var transport = new Transport((_, _) => Json("""
                {"ok":true,"data":[
                  {"bgmId":123,"nameCn":"测试","name":"Test","date":null,"seasonYear":null,"totalEpisodes":null,"image":null},
                  {"bgmId":123,"nameCn":"重复"},{"bgmId":-1,"nameCn":"无效"}
                ]}
                """));
            using var client = new AniBtClient(transport);
            var result = client.SearchAsync("测试 & Anime", 1, default).GetAwaiter().GetResult();
            Assert.Equal(1, result.Items.Count);
            Assert.Null(result.Items[0].Year);
            Assert.Null(result.Items[0].EpisodeCount);
            Assert.Null(result.Items[0].ImageUrl);
            Assert.False(result.HasMore);
            Assert.Contains("q=" + Uri.EscapeDataString("测试 & Anime") + "&limit=25", transport.Requests[0].Url);
            Assert.Equal(0, client.SearchAsync("测试 & Anime", 2, default).GetAwaiter().GetResult().Items.Count);
            Assert.Equal(1, transport.Requests.Count);
        });

        Test("AniBT 不把错误信封或 HTML 页面当成空搜索结果", () =>
        {
            foreach (var response in new[]
            {
                Json("{\"ok\":false,\"error\":{\"code\":\"SEARCH_UNAVAILABLE\"}}"),
                Response("<html>edge check</html>", "text/html")
            })
            {
                using var transport = new Transport((_, _) => response);
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => client.SearchAsync("Test", 1, default).GetAwaiter().GetResult());
            }
        });

        Test("AniBT RSS 仅接受同 Bangumi 身份和本站对应发布种子", () =>
        {
            var items = Release("rel_one", "123", "https://anibt.net/api/torrent/rel_one.torrent")
                + Release("rel_two", "456", "https://anibt.net/api/torrent/rel_two.torrent")
                + Release("rel_three", "123", "https://other.invalid/api/torrent/rel_three.torrent")
                + Release("rel_four", "", "https://anibt.net/api/torrent/rel_four.torrent")
                + Release("rel_five", "123", "https://anibt.net/api/torrent/rel_other.torrent")
                + Release("rel_six", "123", "https://anibt.net/api/torrent/rel_six.torrent?key=secret")
                + Release("rel_seven", "123", "https://name:password@anibt.net/api/torrent/rel_seven.torrent");
            using var transport = new Transport((_, _) => Xml(Feed(items)));
            using var client = new AniBtClient(transport);
            var result = client.ReleasesAsync(new AniBtAnime("ani_one", 123, "测试"), default).GetAwaiter().GetResult();
            Assert.Equal(1, result.Count);
            Assert.Equal("https://anibt.net/api/torrent/rel_one.torrent", result[0].DownloadUri.AbsoluteUri);
            Assert.Equal("https://anibt.net/release/rel_one", result[0].PageUri.AbsoluteUri);
            Assert.Equal(1024L, result[0].Size);
            Assert.Equal("01", result[0].EpisodeKey);
            Assert.NotNull(result[0].PublishedAt);
            Assert.Equal(TimeSpan.FromHours(8), result[0].PublishedAt!.Value.Offset);
            Assert.Equal(1, transport.Requests.Count, "只读取 RSS，不请求种子");
        });

        Test("AniBT RSS 禁用 DTD 与外部实体", () =>
        {
            using var transport = new Transport((_, _) => Xml("<!DOCTYPE rss [<!ENTITY external SYSTEM 'file:///C:/private.txt'>]><rss><channel><title>&external;</title></channel></rss>"));
            using var client = new AniBtClient(transport);
            Assert.Throws<AniBtException>(() => client.ReleasesAsync(new AniBtAnime("ani_one", 123, "测试"), default).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
        });

        Test("AniBT 限流保留 Retry-After 且不自动重发", () =>
        {
            using var transport = new Transport((_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
                return response;
            });
            using var client = new AniBtClient(transport);
            var error = Assert.Catch<AniBtException>(() => client.SearchAsync("Test", 1, default).GetAwaiter().GetResult());
            Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(12), error.RetryAfter);
            Assert.Equal(1, transport.Requests.Count);
        });

        Test("AniBT 只跟随同路径本站 RSS 规范化跳转", () =>
        {
            using var transport = new Transport((_, index) => index == 1
                ? Redirect("/rss/anime.xml?bgmId=123")
                : Xml(Feed(Release("rel_one", "123", "https://anibt.net/api/torrent/rel_one.torrent"))));
            using var client = new AniBtClient(transport);
            Assert.Equal(1, client.ReleasesAsync(new AniBtAnime("ani_one", 123, "测试"), default).GetAwaiter().GetResult().Count);
            Assert.Equal(2, transport.Requests.Count);
            Assert.Equal("https://anibt.net/rss/anime.xml?bgmId=123", transport.Requests[1].Url);
        });

        Test("AniBT 跨站或查询转写入路径的跳转均不跟随", () =>
        {
            foreach (var target in new[] { "https://other.invalid/rss/anime.xml?bgmId=123", "/api/releases/publish" })
            {
                using var transport = new Transport((_, _) => Redirect(target));
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => client.ReleasesAsync(new AniBtAnime("ani_one", 123, "测试"), default).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Requests.Count);
            }
        });

        Test("AniBT 不重放匹配 POST 的重定向", () =>
        {
            using var transport = new Transport((_, _) => Redirect("/api/animes/torrent-match"));
            using var client = new AniBtClient(transport);
            Assert.Throws<AniBtException>(() => client.MatchAsync("Test.mkv", default).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
        });

        Test("AniBT 主动取消保持取消语义且不发网络请求", () =>
        {
            using var transport = new Transport((_, _) => Json(MatchResponse));
            using var client = new AniBtClient(transport);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => client.MatchAsync("Test.mkv", cancellation.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => client.SearchAsync("Test", 2, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests.Count);
        });

        Test("AniBT 传输超时与网络失败有明确错误且不重试", () =>
        {
            foreach (var failure in new Exception[] { new TaskCanceledException(), new HttpRequestException() })
            {
                using var transport = new Transport((_, _) => throw failure);
                using var client = new AniBtClient(transport);
                var error = Assert.Catch<AniBtException>(() => client.SearchAsync("Test", 1, default).GetAwaiter().GetResult());
                Assert.Contains(failure is TaskCanceledException ? "超时" : "无法连接", error.Message);
                Assert.Equal(1, transport.Requests.Count);
            }
        });
    }

    private static string Feed(string items) => "<rss xmlns:anibt=\"https://anibt.net/xmlns/rss/1.0/\"><channel>" + items + "</channel></rss>";

    private static string Release(string id, string bgmId, string url) =>
        "<item><title>Test Anime - 01</title><guid>" + id + "</guid><anibt:bgmId>" + bgmId + "</anibt:bgmId>"
        + "<enclosure url=\"" + url + "\" length=\"1024\" type=\"application/x-bittorrent\"/>"
        + "<anibt:episodeKey>01</anibt:episodeKey><pubDate>Sat, 10 Oct 2026 12:00:00 +0800</pubDate></item>";

    private static HttpResponseMessage Json(string body) => Response(body, "application/json");
    private static HttpResponseMessage Xml(string body) => Response(body, "application/xml");
    private static HttpResponseMessage Response(string body, string type) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

    private static HttpResponseMessage Redirect(string target)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
        response.Headers.Location = new Uri(target, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class Transport(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Method, string Url, string Body, bool HasCredentials)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, body,
                request.Headers.Authorization is not null || request.Headers.Contains("Cookie") || request.Headers.Contains("X-Emby-Token")));
            return respond(request, Requests.Count);
        }
    }
}
