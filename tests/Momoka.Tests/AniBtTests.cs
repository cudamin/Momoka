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
    internal const string CatalogDetail = """
        {"initialAnime":{"_id":"ani_one","bgmId":123,"title":{"chinese":"测试","native":"Test"},
        "episodes":15,"format":"TV","description":"简介正文"},"initialExternalIds":[
        {"source":"anidb","externalId":"42","url":"https://untrusted.invalid/"},
        {"source":"mal","externalId":"javascript:bad","url":"https://untrusted.invalid/"}]}
        """;

    internal const string CatalogGroups = """
        {"ok":true,"data":{"bgmId":123,"animeId":"ani_one","groups":[
        {"groupId":"org_one","slug":"group & one","name":"字幕组","totalReleases":40,"publishedEpisodes":[4,1,2,3,3,-1,"bad"],
        "items":[{"releaseId":"rel_one","title":"测试 - 03","size":12345,"publishedAt":1792889728859,
        "episodeKey":"3","resolution":"1080p","format":"MP4","language":["CHT"],"subtitle":"EMBEDDED",
        "customTags":["WEB-DL","AAC"],"url":"https://untrusted.invalid/","torrentStorageId":"fixture.torrent"},
        {"releaseId":"../../escape","title":"拒绝"},{"releaseId":"rel_one","title":"重复"}]}]}}
        """;

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
        Test("AniBT 默认候选：自动识别优先，否则显示第一个", () =>
        {
            var first = new AniBtAnime("ani_first", 123, "第一候选");
            var second = new AniBtAnime("ani_second", 456, "第二候选");
            Assert.Equal(first, new AniBtMatch([first, second], first, "auto", "high", "", 1).DefaultCandidate);
            Assert.Equal(first, new AniBtMatch([first, second], null, "manual", "low", "", 1).DefaultCandidate);
            Assert.Null(new AniBtMatch([], null, "none", "", "", null).DefaultCandidate);
        });

        Test("AniBT Emby 识别候选：标题完全一致优先，全角标点也算同一个", () =>
        {
            var wrong = new RemoteSearchResult { Name = "女友的闺蜜", ProductionYear = 2026 };
            var exact = new RemoteSearchResult { Name = "学生会也有洞！", ProductionYear = 2025 };
            var picked = AniBtService.PickIdentifyResult([wrong, exact], ["学生会也有洞!"], 2026);
            Assert.True(ReferenceEquals(exact, picked));
            Assert.Equal("学生会也有洞", AniBtService.NormalizeTitle("学生会也有洞！"));
            // R-037 改契约：非空/无关首条不再是识别授权；原来的精确标题及 ±1 年容差仍保留。
            Assert.Null(AniBtService.PickIdentifyResult([wrong], ["学生会也有洞!"], 2026));
            Assert.Null(AniBtService.PickIdentifyResult(
                [new RemoteSearchResult { Name = exact.Name, ProductionYear = 2010 }], ["学生会也有洞!"], 2026));
            Assert.Null(AniBtService.PickIdentifyResult([], ["学生会也有洞!"], 2026));
        });

        Test("AniBT 发布短标签：单集号、合集与相对时间", () =>
        {
            Assert.Equal("01", AniBtReleaseLabels.EpisodeBadge("1"));
            Assert.Equal("12", AniBtReleaseLabels.EpisodeBadge("12"));
            Assert.Equal("004", AniBtReleaseLabels.EpisodeBadge("004"));
            Assert.Equal("合集", AniBtReleaseLabels.EpisodeBadge(""));
            Assert.Equal("合集", AniBtReleaseLabels.EpisodeBadge("batch"));
            Assert.Equal("未标集号", AniBtReleaseLabels.EpisodeChoice(null));
            Assert.Equal("SP1", AniBtReleaseLabels.EpisodeChoice(" SP1 "));
            Assert.Equal("未标字幕组", AniBtReleaseLabels.Group(" "));
            Assert.Equal("未标清晰度", AniBtReleaseLabels.Resolution(null));

            var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.FromHours(8));
            Assert.Equal("刚刚", AniBtReleaseLabels.Published(now - TimeSpan.FromSeconds(30), now));
            Assert.Equal("5 分钟前", AniBtReleaseLabels.Published(now - TimeSpan.FromMinutes(5), now));
            Assert.Equal("3 小时前", AniBtReleaseLabels.Published(now - TimeSpan.FromHours(3), now));
            Assert.Equal("2 天前", AniBtReleaseLabels.Published(now - TimeSpan.FromDays(2), now));
            Assert.Equal("2026-10-09", AniBtReleaseLabels.Published(now - TimeSpan.FromDays(10), now));
            Assert.Equal("2026-10-20", AniBtReleaseLabels.Published(now + TimeSpan.FromDays(1), now));
            Assert.Equal("发布时间未提供", AniBtReleaseLabels.Published(null, now));
        });

        Test("AniBT RSS：多条发布保留集号、字幕组与清晰度", () =>
        {
            const string feed = """
                <rss xmlns:anibt="https://anibt.net/xmlns/rss/1.0/"><channel><item>
                  <title>[离线字幕组] 离线示例动画 - 01 [1080p][HEVC]</title><guid>rel_one</guid>
                  <anibt:bgmId>123</anibt:bgmId><anibt:groupName>离线字幕组</anibt:groupName>
                  <anibt:resolution>1080p</anibt:resolution><anibt:episodeKey>01</anibt:episodeKey>
                  <enclosure url="https://anibt.net/api/torrent/rel_one.torrent" length="1234567890" />
                  <pubDate>Sun, 18 Oct 2026 12:00:00 +0800</pubDate>
                </item><item>
                  <title>[另一个字幕组] 离线示例动画 - 02 [720p][HEVC]</title><guid>rel_two</guid>
                  <anibt:bgmId>123</anibt:bgmId><anibt:groupName>另一个字幕组</anibt:groupName>
                  <anibt:resolution>720p</anibt:resolution><anibt:episodeKey>02</anibt:episodeKey>
                  <enclosure url="https://anibt.net/api/torrent/rel_two.torrent" length="456789012" />
                  <pubDate>Sat, 17 Oct 2026 12:00:00 +0800</pubDate>
                </item><item>
                  <title>[离线字幕组] 离线示例动画 合集 [1080p][HEVC]</title><guid>rel_batch</guid>
                  <anibt:bgmId>123</anibt:bgmId><anibt:groupName>离线字幕组</anibt:groupName>
                  <anibt:resolution>1080p</anibt:resolution><anibt:episodeKey></anibt:episodeKey>
                  <enclosure url="https://anibt.net/api/torrent/rel_batch.torrent" length="9876543210" />
                  <pubDate>Sat, 10 Oct 2026 12:00:00 +0800</pubDate>
                </item></channel></rss>
                """;
            using var transport = new Transport((_, _) => Xml(feed));
            using var client = new AniBtClient(transport);
            var result = client.ReleasePageAsync(new AniBtAnime("", 123, "离线示例动画"), default).GetAwaiter().GetResult();
            Assert.Equal(3, result.Items.Count);
            Assert.Equal("01", result.Items[0].EpisodeKey);
            Assert.Equal("离线字幕组", result.Items[0].GroupName);
            Assert.Equal("1080p", result.Items[0].Resolution);
            Assert.Equal("02", result.Items[1].EpisodeKey);
            Assert.Equal("", result.Items[2].EpisodeKey);
        });

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
                {"page":[
                  {"_id":"ani_one","bgmId":123,"title":{"chinese":"测试","native":"Test"},"seasonYear":null,"episodes":null,"coverImage":null},
                  {"_id":"ani_one","bgmId":123,"title":{"chinese":"重复"}},
                  {"_id":"ani_invalid","bgmId":-1,"title":{"chinese":"无效"}}
                ],"isDone":true,"continueCursor":""}
                """));
            using var client = new AniBtClient(transport);
            var result = client.SearchAsync("测试 & Anime", 1, default).GetAwaiter().GetResult();
            Assert.Equal(1, result.Items.Count);
            Assert.Null(result.Items[0].Year);
            Assert.Null(result.Items[0].EpisodeCount);
            Assert.Null(result.Items[0].ImageUrl);
            Assert.False(result.HasMore);
            Assert.Contains("api/animes/search?q=" + Uri.EscapeDataString("测试 & Anime") + "&limit=24", transport.Requests[0].Url);
            Assert.Equal(0, client.SearchAsync("测试 & Anime", 2, default).GetAwaiter().GetResult().Items.Count);
            Assert.Equal(1, transport.Requests.Count);
        });

        Test("AniBT 目录保留海报字段并回传不透明游标", () =>
        {
            using var transport = new Transport((_, index) => Json(index == 1 ? """
                {"page":[{"_id":"ani_one","bgmId":123,"title":{"chinese":"测试","native":"原名","english":"English"},
                "coverImage":{"large":"https://r2.anibt.net/cover.jpg"},"format":"TV","status":"RELEASING",
                "averageScore":75,"season":"FALL","seasonYear":2026,"episodes":15,"genres":["喜剧","校园"],
                "officialSite":"https://example.invalid/"}],"isDone":false,"continueCursor":"opaque & page:2"}
                """ : """{"page":[],"isDone":true,"continueCursor":""}"""));
            using var client = new AniBtClient(transport);
            var first = client.SearchCatalogAsync("学生会", null, default).GetAwaiter().GetResult();
            Assert.True(first.HasMore);
            var anime = first.Items.Single();
            Assert.Equal(7.5, anime.Score);
            Assert.Equal("TV", anime.Format);
            Assert.Equal("RELEASING", anime.Status);
            Assert.Equal("English", anime.EnglishTitle);
            Assert.Equal("FALL", anime.Season);
            Assert.Equal("https://r2.anibt.net/cover.jpg", anime.ImageUrl);
            Assert.True(anime.Genres.SequenceEqual(["喜剧", "校园"]));
            var next = client.SearchCatalogAsync("学生会", first.Cursor, default).GetAwaiter().GetResult();
            Assert.False(next.HasMore);
            Assert.Null(next.Cursor);
            Assert.Contains("&cursor=opaque%20%26%20page%3A2", transport.Requests[1].Url);
            Assert.True(transport.Requests.All(request => request.Method == "GET" && !request.HasCredentials));
        });

        Test("AniBT 目录拒绝损坏或重复游标，非法可选数据不伪造显示值", () =>
        {
            foreach (var response in new[]
            {
                """{"page":[],"isDone":false,"continueCursor":""}""",
                """{"page":[],"isDone":false,"continueCursor":"same"}""",
                """{"page":[],"continueCursor":"next"}"""
            })
            {
                using var transport = new Transport((_, _) => Json(response));
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => client.SearchCatalogAsync("动画", "same", default).GetAwaiter().GetResult());
            }
            using var invalidTransport = new Transport((_, _) => Json("""
                {"page":[{"_id":"ani_one","bgmId":123,"title":{"native":"原名"},"averageScore":101,
                "officialSite":"javascript:alert(1)","coverImage":{"large":"https://user:secret@example.invalid/a"}}],"isDone":true}
                """));
            using var invalidClient = new AniBtClient(invalidTransport);
            var item = invalidClient.SearchCatalogAsync("动画", null, default).GetAwaiter().GetResult().Items.Single();
            Assert.Null(item.Score);
            Assert.Null(item.OfficialSite);
            Assert.Null(item.ImageUrl);
            Assert.Equal("", item.Status);
        });

        Test("AniBT 分组先规范化番剧身份，完整进度与最近资源分开", () =>
        {
            using var transport = new Transport((_, index) => Json(index == 1 ? CatalogDetail : CatalogGroups));
            using var client = new AniBtClient(transport);
            var result = client.GroupPageAsync(new AniBtAnime("old", 456, "旧编号"), default).GetAwaiter().GetResult();
            Assert.Equal(123L, result.Anime.BgmId);
            Assert.Equal("简介正文", result.Anime.Description);
            Assert.Equal("https://anidb.net/anime/42", result.Anime.ExternalLinks.Single().Uri.AbsoluteUri);
            Assert.True(transport.Requests[0].Url.EndsWith("api/public/v1/anime?bgmId=456", StringComparison.Ordinal));
            Assert.True(transport.Requests[1].Url.EndsWith("api/anime/groups?bgmId=123", StringComparison.Ordinal));
            var group = result.Groups.Single();
            Assert.True(group.PublishedEpisodes.SequenceEqual([1, 2, 3, 4]));
            Assert.Equal(40, group.TotalReleases);
            Assert.Equal(1, group.Items.Count);
            Assert.Equal("https://anibt.net/rss/anime.xml?bgmId=123&groupSlug=group%20%26%20one", group.RssUri!.AbsoluteUri);
            var release = group.Items.Single();
            Assert.Equal("MP4", release.Format);
            Assert.True(release.Languages!.SequenceEqual(["CHT"]));
            Assert.Equal("EMBEDDED", release.Subtitle);
            Assert.True(release.Tags!.SequenceEqual(["WEB-DL", "AAC"]));
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792889728859), release.PublishedAt);
            Assert.Equal("https://anibt.net/api/torrent/rel_one.torrent", release.DownloadUri.AbsoluteUri);
            Assert.True(transport.Requests.All(request => request.Method == "GET" && !request.HasCredentials));
        });

        Test("AniBT 分组拒绝跨番剧或损坏响应，不吞成空资源", () =>
        {
            foreach (var groups in new[]
            {
                CatalogGroups.Replace("\"animeId\":\"ani_one\"", "\"animeId\":\"another\"", StringComparison.Ordinal),
                CatalogGroups.Replace("\"bgmId\":123", "\"bgmId\":456", StringComparison.Ordinal),
                CatalogGroups.Replace("\"items\":[", "\"missingItems\":[", StringComparison.Ordinal)
            })
            {
                using var transport = new Transport((_, index) => Json(index == 1 ? CatalogDetail : groups));
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => client.GroupPageAsync(new AniBtAnime("ani_one", 123, "测试"), default).GetAwaiter().GetResult());
            }
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
