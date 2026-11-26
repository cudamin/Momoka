using System.Net;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotScrobblingTests
{
    private const string Series = """
        {"media_source":"themoviedb","media_id":42,"type":"电视剧","title":"示例剧","original_title":"Fixture Show","year":2024}
        """;
    private const string Site = """
        {"media_source":"douban","media_id":84,"type":"电视剧","title":"示例剧 第二季","tmdb_id":42,"season":1}
        """;
    private const string Mapping = """
        {"media_source":"douban","media_id":"84","type":"tv","season":1,"tmdb_id":42,"tmdb_type":"tv","tmdb_season":2}
        """;
    private const string Episode = """[{"id":700,"show_id":42,"season_number":2,"episode_number":10}]""";
    private const string SeriesDetail = "/media/42?";
    private const string SiteDetail = "/media/84?";
    private const string MappingPath = "/media/mapping?";
    private const string EpisodePath = "/tmdb/42/2";
    private const string SearchPath = "/media/search?";
    private const string Secret = "unique-fixture-not-a-real-key";

    internal static void Register()
    {
        Test("MP 打格子：精确来源详情、原始季集和条目内季号各自校验，只读确认豆瓣篇目", () =>
        {
            using var transport = new StubTransport()
                .Answer(SeriesDetail, Series.Replace("\"year\":2024", "\"year\":2024,\"douban_id\":84", StringComparison.Ordinal))
                .Answer(EpisodePath, Episode).Answer(SiteDetail, Site).Answer(MappingPath, Mapping);
            using var fixture = new ConnectedFixture(transport);
            var result = fixture.Resolve() ?? throw new AssertionException("应唯一确定");
            Assert.Equal("themoviedb", result.MediaSource);
            Assert.Equal("42", result.MediaId);
            Assert.Equal(42, result.TmdbId);
            Assert.Equal(700L, result.TmdbEpisodeId);
            Assert.Equal(2, result.Season);
            Assert.Equal(10, result.Episode);
            Assert.Equal(84L, result.DoubanId);
            Assert.Null(result.DoubanEpisode, "共享关系不包含条目内集号");
            Assert.Null(result.TmdbSeasonLastEpisode, "仅一个跳号集时，不能确定季末");
            Assert.True(fixture.Service.IsCurrentScrobblingConnection(result.ConnectionStamp));
            Assert.Contains("?media_source=themoviedb&type_name=%E7%94%B5%E8%A7%86%E5%89%A7", transport.Only(SeriesDetail).Url);
            Assert.Contains("?media_source=douban&type_name", transport.Only(SiteDetail).Url);
            Assert.Contains("?media_source=douban&media_id=84&type=tv&season=1", transport.Only(MappingPath).Url);
            foreach (var read in transport.SentTo("/api/v1/").Where(request => !request.Url.Contains("/login/", StringComparison.Ordinal)))
            {
                Assert.Equal("GET", read.Method);
                Assert.Equal("", read.Body);
                Assert.DoesNotContain(Secret, read.Url);
                Assert.DoesNotContain("episode_group", read.Url);
            }
        });
        foreach (var (label, detail) in new[]
        {
            ("主来源冲突", Series.Replace("themoviedb", "douban", StringComparison.Ordinal)),
            ("编号冲突", Series.Replace("\"media_id\":42", "\"media_id\":43", StringComparison.Ordinal)),
            ("主编号缺失", Series.Replace("\"media_id\":42", "\"tmdb_id\":42", StringComparison.Ordinal)),
            ("辅助编号冲突", Series.Replace("\"year\":2024", "\"year\":2024,\"tmdb_id\":43", StringComparison.Ordinal)),
            ("媒体类型冲突", Series.Replace("电视剧", "电影", StringComparison.Ordinal)),
            ("库的 TVDB 编号冲突", Series.Replace("\"year\":2024", "\"year\":2024,\"tvdb_id\":5", StringComparison.Ordinal))
        })
        {
            Test("MP 打格子：详情拒绝" + label, () =>
            {
                using var transport = new StubTransport().Answer(SeriesDetail, detail).Answer(EpisodePath, Episode);
                using var fixture = new ConnectedFixture(transport);
                Assert.Null(fixture.Resolve(new Dictionary<string, string> { ["Tmdb"] = "42", ["Tvdb"] = "6" }));
                Assert.Equal(0, transport.Count(EpisodePath));
            });
        }
        foreach (var (label, episodes) in new[]
        {
            ("重复集", "[" + Episode[1..^1] + "," + Episode[1..^1] + "]"),
            ("目标季不符", Episode.Replace("\"season_number\":2", "\"season_number\":1", StringComparison.Ordinal)),
            ("未收录目标集", Episode.Replace("\"episode_number\":10", "\"episode_number\":11", StringComparison.Ordinal)),
            ("所属父剧不符", Episode.Replace("\"show_id\":42", "\"show_id\":43", StringComparison.Ordinal)),
            ("显式父剧无效", Episode.Replace("\"show_id\":42", "\"show_id\":0", StringComparison.Ordinal)),
            ("集编号无效", Episode.Replace("\"id\":700", "\"id\":0", StringComparison.Ordinal)),
            ("空季", "[]")
        })
        {
            Test("MP 打格子：仅同步 Bangumi 时拒绝" + label, () =>
            {
                using var transport = new StubTransport().Answer(SeriesDetail, Series).Answer(EpisodePath, episodes);
                using var fixture = new ConnectedFixture(transport);
                Assert.Null(fixture.Resolve(includeBangumi: true, includeDouban: false));
                Assert.Equal(0, transport.SentTo(SearchPath).Count);
            });
        }
        foreach (var (label, mapping) in new[]
        {
            ("对应不同季", Mapping.Replace("\"tmdb_season\":2", "\"tmdb_season\":1", StringComparison.Ordinal)),
            ("对应不同来源", Mapping.Replace("douban", "bangumi", StringComparison.Ordinal)),
            ("对应不同来源季", Mapping.Replace("\"season\":1", "\"season\":2", StringComparison.Ordinal)),
            ("对应不同剧", Mapping.Replace("\"tmdb_id\":42", "\"tmdb_id\":43", StringComparison.Ordinal)),
            ("缺少目标季", Mapping.Replace("\"tmdb_season\":2", "\"tmdb_season\":null", StringComparison.Ordinal)),
            ("服务没找到对应关系", "null")
        })
        {
            Test("MP 打格子：站点关系" + label + "只保留核实过的 TMDB 季集", () =>
            {
                using var transport = new StubTransport().Answer(SeriesDetail, Series).Answer(EpisodePath, Episode)
                    .Answer(SearchPath, "[" + Site + "]").Answer(SiteDetail, Site).Answer(MappingPath, mapping);
                using var fixture = new ConnectedFixture(transport);
                var result = fixture.Resolve() ?? throw new AssertionException("应保留独立确认的 TMDB 季集");
                Assert.Equal(700L, result.TmdbEpisodeId);
                Assert.Null(result.DoubanId);
                Assert.DoesNotContain("media/mapping/match", string.Join(',', transport.SentTo("/media/").Select(request => request.Url)));
            });
        }
        Test("MP 打格子：豆瓣整剧关系季号为空时，核实唯一正季及同名同年条目后识别作品", () =>
        {
            const string seasons = """
                {"number_of_seasons":1,"season_info":[{"season_number":0},{"season_number":1}]}
                """;
            const string site = """
                {"media_source":"douban","media_id":"84","type":"电视剧","title":"示例剧","year":2024}
                """;
            const string mapping = """
                {"media_source":"douban","media_id":"84","type":"tv","season":null,"tmdb_type":"tv","tmdb_id":42,"tmdb_season":null}
                """;
            using var transport = new StubTransport().Answer(SearchPath, "[" + site + "]").Answer(SiteDetail, site)
                .Answer(MappingPath, mapping);
            using var fixture = new ConnectedFixture(transport);
            void SetSeasons(string metadata) => transport.Answer(SeriesDetail, Series[..^1] + "," + metadata[1..]);
            long? Identify() => fixture.Resolve(season: 1, episode: 0)?.DoubanId;

            SetSeasons(seasons);
            Assert.Equal(84L, Identify());
            Assert.Equal(0, transport.SentTo("/tmdb/").Count);
            transport.Answer(SiteDetail, site[..^1] + ",\"season\":1}");
            Assert.Equal(84L, Identify());
            transport.Answer(SiteDetail, site);

            foreach (var invalid in new[]
            {
                seasons.Replace("seasons\":1", "seasons\":2", StringComparison.Ordinal),
                seasons.Replace("number\":0", "number\":2", StringComparison.Ordinal),
                seasons.Replace("number\":0", "number\":1", StringComparison.Ordinal),
                seasons.Replace("number\":0", "number\":null", StringComparison.Ordinal)
            })
            {
                SetSeasons(invalid);
                Assert.Null(Identify());
            }
            SetSeasons(seasons);
            foreach (var invalid in new[]
            {
                mapping.Replace("tmdb_id\":42", "tmdb_id\":43", StringComparison.Ordinal),
                mapping.Replace("tmdb_type\":\"tv", "tmdb_type\":\"movie", StringComparison.Ordinal),
                mapping.Replace("tmdb_season\":null", "tmdb_season\":0", StringComparison.Ordinal),
                mapping.Replace("tmdb_season\":null", "tmdb_season\":2", StringComparison.Ordinal),
                mapping.Replace("\"season\":null", "\"season\":2", StringComparison.Ordinal)
            })
            {
                transport.Answer(MappingPath, invalid);
                Assert.Null(Identify());
            }
            transport.Answer(MappingPath, mapping);
            transport.Answer(SiteDetail, site.Replace("示例剧", "另一部剧", StringComparison.Ordinal));
            Assert.Null(Identify());
        });
        Test("MP 打格子：无库 ID 时唯一标题及年份须回读，豆瓣第二季用明确季名定位并核验共享关系", () =>
        {
            using var transport = new StubTransport()
                .Answer("&media_source=themoviedb", "[" + Series + "]")
                .Answer("&media_source=douban", "[" + Site + "]")
                .Answer(SeriesDetail, Series.Replace("\"year\":2024", "\"year\":2024,\"season_info\":[{\"season_number\":2,\"name\":\"独立季名\"}]", StringComparison.Ordinal))
                .Answer(EpisodePath, Episode).Answer(SiteDetail, Site).Answer(MappingPath, Mapping);
            using var fixture = new ConnectedFixture(transport);
            var result = fixture.Resolve(new Dictionary<string, string>()) ?? throw new AssertionException("应识别父剧及对应篇目");
            Assert.Equal(84L, result.DoubanId);
            Assert.Equal(1, transport.Count(SeriesDetail));
            Assert.Equal(2, transport.Count("&media_source=themoviedb"));
            Assert.Contains(Uri.EscapeDataString("独立季名"), transport.Only("&media_source=douban").Url);
        });
        Test("MP 打格子：IMDb 原编号识别父剧，不用标题首条结果补猜", () =>
        {
            const string native = """
                {"media_source":"imdb","media_id":"tt0123456","imdb_id":"tt0123456","tmdb_id":42,"type":"电视剧"}
                """;
            using var transport = new StubTransport().Answer("/media/tt0123456?", native)
                .Answer(SeriesDetail, Series).Answer(EpisodePath, Episode)
                .Answer(SearchPath, "[]");
            using var fixture = new ConnectedFixture(transport);
            var result = fixture.Resolve(new Dictionary<string, string> { ["Imdb"] = "tt0123456" });
            Assert.Equal(700L, result?.TmdbEpisodeId);
            Assert.Contains("media_source=imdb", transport.Only("/media/tt0123456?").Url);
            Assert.Equal(0, transport.Count("&media_source=themoviedb"));
        });
        Test("MP 打格子：同名同年份多候选，无论搜索返回顺序均不采用", () =>
        {
            var other = Series.Replace("\"media_id\":42", "\"media_id\":43", StringComparison.Ordinal);
            using var transport = new StubTransport().Answer("&media_source=themoviedb", "[" + Series + "," + other + "]");
            using var fixture = new ConnectedFixture(transport);
            Assert.Null(fixture.Resolve(new Dictionary<string, string>()));
            Assert.Equal(0, transport.SentTo(SeriesDetail).Count);
        });
        Test("MP 打格子：只有豆瓣原生编号的第一季只核实原作品，不借 TMDB 坐标", () =>
        {
            using var transport = new StubTransport().Answer(SiteDetail, Site);
            using var fixture = new ConnectedFixture(transport);
            var ids = new Dictionary<string, string> { ["Douban"] = "84" };
            Assert.Null(fixture.Resolve(ids));
            var result = fixture.Resolve(ids, season: 1) ?? throw new AssertionException("应保留库已有且精确读回的豆瓣编号");
            Assert.Equal("douban", result.MediaSource);
            Assert.Equal(84L, result.DoubanId);
            Assert.Null(result.TmdbId);
            Assert.Null(result.TmdbEpisodeId);
            Assert.Null(result.DoubanEpisode);
            Assert.Equal(0, transport.SentTo(MappingPath).Count);
            Assert.Equal(2, transport.Count(SiteDetail));
        });
        Test("MP 打格子：季集未知时，只确认媒体库直给的豆瓣作品", () =>
        {
            using var transport = new StubTransport().Answer(SiteDetail, Site);
            using var fixture = new ConnectedFixture(transport);
            var ids = new Dictionary<string, string> { ["Douban"] = "84", ["Tmdb"] = "42" };
            var result = fixture.Resolve(ids, season: 0, episode: 0, includeBangumi: true);
            Assert.Equal(84L, result?.DoubanId);
            Assert.Equal(0, result?.Season);
            Assert.Equal(0, result?.Episode);
            Assert.Null(result?.TmdbEpisodeId);
            Assert.Null(result?.TmdbSeasonLastEpisode);
            Assert.Null(result?.BangumiId);
            Assert.Equal(0, transport.SentTo("/tmdb/").Count);
            Assert.Equal(0, transport.Count(SeriesDetail));
            Assert.Equal(1, transport.Count(SiteDetail));

            transport.Answer(SiteDetail, Site.Replace("\"media_id\":84", "\"media_id\":85", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids, season: 0, episode: 0));
            Assert.Null(fixture.Resolve(season: 0, episode: 0));
        });
        Test("MP 打格子：只有站点原生编号时，读取明确同季的后续豆瓣作品", () =>
        {
            var seasonTwo = Site.Replace("\"season\":1", "\"season\":2", StringComparison.Ordinal);
            using var transport = new StubTransport().Answer(SiteDetail, seasonTwo);
            using var fixture = new ConnectedFixture(transport);
            var ids = new Dictionary<string, string> { ["Bangumi"] = "85", ["Douban"] = "84" };
            var result = fixture.Resolve(ids, episode: 0, includeBangumi: true);
            Assert.Equal(84L, result?.DoubanId);
            Assert.Equal(2, result?.Season);
            Assert.Null(result?.DoubanEpisode);
            Assert.Null(result?.TmdbSeasonLastEpisode);
            Assert.Null(result?.BangumiId);
            Assert.Equal(1, transport.Count(SiteDetail));
            Assert.Equal(0, transport.Count("/media/85?"));
            Assert.Equal(0, transport.Count(MappingPath));
            Assert.Equal(10, fixture.Resolve(ids)?.DoubanEpisode);

            transport.Answer(SiteDetail, seasonTwo.Replace("\"season\":2", "\"season\":null", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids, episode: 0));
            transport.Answer(SiteDetail, seasonTwo.Replace("\"media_id\":84", "\"media_id\":86", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids, episode: 0));
            transport.Answer(SiteDetail, seasonTwo.Replace("\"season\":2", "\"season\":2,\"bangumi_id\":86", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids, episode: 0));
        });
        Test("MP 打格子：父剧豆瓣编号映射到同一剧的其他季时，读取有当前季映射的独立作品", () =>
        {
            const string first = """
                {"media_source":"douban","media_id":"83","type":"电视剧","title":"示例剧","tmdb_id":42,"season":1}
                """;
            const string firstDetail = "/media/83?";
            const string firstMapping = "&media_id=83&";
            const string currentMapping = "&media_id=84&";
            var tmdb = Series.Replace("\"year\":2024",
                "\"year\":2024,\"season_info\":[{\"season_number\":2,\"name\":\"独立季名\"}]", StringComparison.Ordinal);
            var redirect = Mapping.Replace("\"84\"", "\"83\"", StringComparison.Ordinal)
                .Replace("\"tmdb_season\":2", "\"tmdb_season\":1", StringComparison.Ordinal);
            using var transport = new StubTransport().Answer(SeriesDetail, tmdb).Answer(EpisodePath, Episode)
                .Answer(firstDetail, first).Answer(firstMapping, redirect)
                .Answer("&media_source=douban", "[" + Site + "]").Answer(SiteDetail, Site).Answer(currentMapping, Mapping);
            using var fixture = new ConnectedFixture(transport);
            var ids = new Dictionary<string, string> { ["Tmdb"] = "42", ["Douban"] = "83" };
            Assert.Equal(84L, fixture.Resolve(ids)?.DoubanId);
            Assert.Contains(Uri.EscapeDataString("独立季名"), transport.Only("&media_source=douban").Url);

            transport.Answer(currentMapping, "null");
            Assert.Null(fixture.Resolve(ids)?.DoubanId);
            transport.Answer(currentMapping, Mapping.Replace("\"tmdb_id\":42", "\"tmdb_id\":43", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids)?.DoubanId);

            transport.Answer(currentMapping, Mapping);
            transport.Answer(SiteDetail, Site.Replace("\"season\":1", "\"season\":1,\"douban_id\":83", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids)?.DoubanId);
            transport.Answer(SiteDetail, Site);
            var competing = Site.Replace("\"media_id\":84", "\"media_id\":85", StringComparison.Ordinal);
            transport.Answer("&media_source=douban", "[" + Site + "," + competing + "]")
                .Answer("/media/85?", competing)
                .Answer("&media_id=85&", Mapping.Replace("\"media_id\":\"84\"", "\"media_id\":\"85\"", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve(ids)?.DoubanId);

            transport.Answer(firstMapping, redirect.Replace("\"tmdb_id\":42", "\"tmdb_id\":43", StringComparison.Ordinal));
            var searchCount = transport.Count("&media_source=douban");
            Assert.Null(fixture.Resolve(ids)?.DoubanId);
            Assert.Equal(searchCount, transport.Count("&media_source=douban"));

            transport.Answer(firstMapping, "null");
            Assert.Null(fixture.Resolve(ids)?.DoubanId);
            Assert.Equal(searchCount, transport.Count("&media_source=douban"));
        });
        Test("MP 打格子：末集未知或 TMDB 未收录在播单集时，可靠的豆瓣关系仍可按作品同步", () =>
        {
            using var transport = new StubTransport().Answer(SeriesDetail, Series).Answer(EpisodePath, "[]")
                .Answer(SiteDetail, Site).Answer(MappingPath, Mapping);
            using var fixture = new ConnectedFixture(transport);
            var ids = new Dictionary<string, string> { ["Douban"] = "84", ["Tmdb"] = "42" };
            var unknown = fixture.Resolve(ids, episode: 0);
            Assert.Equal(84L, unknown?.DoubanId);
            Assert.Null(unknown?.TmdbEpisodeId);
            Assert.Null(unknown?.TmdbSeasonLastEpisode);
            Assert.False(unknown?.TmdbSeasonLookupFailed == true);
            Assert.Equal(0, transport.Count(EpisodePath));

            foreach (var missingEpisodes in new[] { "[]", Episode.Replace("\"episode_number\":10", "\"episode_number\":11", StringComparison.Ordinal) })
            {
                transport.Answer(EpisodePath, missingEpisodes);
                var missing = fixture.Resolve(ids, includeBangumi: true);
                Assert.Equal(84L, missing?.DoubanId);
                Assert.Equal(42, missing?.TmdbId);
                Assert.Null(missing?.TmdbEpisodeId);
                Assert.Null(missing?.TmdbSeasonLastEpisode);
                Assert.False(missing?.TmdbSeasonLookupFailed == true);
                Assert.Null(missing?.BangumiId);
            }
            Assert.Equal(2, transport.Count(EpisodePath));

            transport.Fail(EpisodePath, HttpStatusCode.ServiceUnavailable);
            var failedEpisodeRead = fixture.Resolve(ids, includeBangumi: true);
            Assert.Equal(84L, failedEpisodeRead?.DoubanId);
            Assert.True(failedEpisodeRead?.BangumiLookupFailed == true);
            Assert.False(failedEpisodeRead?.DoubanLookupFailed == true);
            Assert.True(failedEpisodeRead?.TmdbSeasonLookupFailed == true);
            Assert.Null(failedEpisodeRead?.TmdbEpisodeId);
        });
        Test("MP 打格子：中心没有关系时，用豆瓣明确的首季标题和年份两次核实", () =>
        {
            const string counterpart = """
                {"media_source":"douban","media_id":"84","type":"电视剧","title":"示例剧","year":"2024"}
                """;
            using var transport = new StubTransport().Answer(SeriesDetail, Series)
                .Answer("/tmdb/42/1", Episode.Replace("\"season_number\":2", "\"season_number\":1", StringComparison.Ordinal))
                .Answer("&media_source=douban", "[" + counterpart + "]").Answer(SiteDetail, counterpart)
                .Fail(MappingPath, HttpStatusCode.NotFound);
            using var fixture = new ConnectedFixture(transport);
            Assert.Equal(84L, fixture.Resolve(season: 1)?.DoubanId);

            var wrongYear = counterpart.Replace("2024", "2023", StringComparison.Ordinal);
            transport.Answer(SiteDetail, wrongYear);
            Assert.Null(fixture.Resolve(season: 1)?.DoubanId);

            var conflict = counterpart.Replace("\"year\":\"2024\"", "\"year\":\"2024\",\"tmdb_id\":43", StringComparison.Ordinal);
            transport.Answer(SiteDetail, conflict);
            Assert.Null(fixture.Resolve(season: 1)?.DoubanId);

            var sameName = counterpart.Replace("84", "85", StringComparison.Ordinal);
            transport.Answer(SiteDetail, counterpart);
            transport.Answer("&media_source=douban", "[" + counterpart + "," + sameName + "]");
            transport.Answer("/media/85?", sameName);
            Assert.Null(fixture.Resolve(season: 1)?.DoubanId);
        });
        Test("MP 打格子：后续季无共享关系时要有独立季名及播出年份，不能只用父剧名", () =>
        {
            const string counterpart = """
                {"media_source":"douban","media_id":"84","type":"电视剧","title":"独立第二季","release_date":"2025-02-01","season":1}
                """;
            var tmdb = Series.Replace("\"year\":2024",
                "\"year\":2024,\"season_info\":[{\"season_number\":2,\"name\":\"独立第二季\",\"air_date\":\"2025-01-13\"}]", StringComparison.Ordinal);
            using var transport = new StubTransport().Answer(SeriesDetail, tmdb).Answer(EpisodePath, Episode)
                .Answer("&media_source=douban", "[" + counterpart + "]").Answer(SiteDetail, counterpart).Answer(MappingPath, "null");
            using var fixture = new ConnectedFixture(transport);
            Assert.Equal(84L, fixture.Resolve()?.DoubanId);

            transport.Answer(SeriesDetail, tmdb.Replace("独立第二季", "示例剧", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve()?.DoubanId);

            transport.Answer(SeriesDetail, tmdb.Replace("2025-01-13", "", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve()?.DoubanId);

            transport.Answer(SeriesDetail, tmdb);
            transport.Answer(MappingPath, Mapping.Replace("\"tmdb_season\":2", "\"tmdb_season\":1", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve()?.DoubanId);
        });
        Test("MP 打格子：内嵌章节关系已收录的请求无需额外查 Bangumi 候选", () =>
        {
            const string series = """
                {"media_source":"themoviedb","media_id":"45790","type":"电视剧","title":"示例剧"}
                """;
            const string episodes = """[{"id":42,"season_number":6,"episode_number":10}]""";
            using var transport = new StubTransport().Answer("/media/45790?", series).Answer("/tmdb/45790/6", episodes);
            using var fixture = new ConnectedFixture(transport);
            var result = fixture.Service.ResolveScrobblingEpisodeAsync("示例剧", null, null,
                new Dictionary<string, string> { ["Tmdb"] = "45790" }, 6, 10, true, false, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(42L, result?.TmdbEpisodeId);
            Assert.Equal(0, transport.SentTo("&media_source=bangumi").Count);
        });
        Test("MP 打格子：一站的识别失败不丢另一站的匹配", () =>
        {
            const string nativeBangumi = """
                {"media_source":"bangumi","media_id":85,"type":"电视剧","season":1}
                """;
            using var transport = new StubTransport().Answer(SiteDetail, Site)
                .Sequence("/media/85?", (HttpStatusCode.ServiceUnavailable, "{\"message\":\"" + Secret + "\"}"));
            using var fixture = new ConnectedFixture(transport);
            var ids = new Dictionary<string, string> { ["Bangumi"] = "85", ["Douban"] = "84" };
            var douban = fixture.Resolve(ids, season: 1, includeBangumi: true);
            Assert.Equal(84L, douban?.DoubanId);
            Assert.True(douban?.BangumiLookupFailed == true);
            Assert.False(douban?.DoubanLookupFailed == true);
            Assert.DoesNotContain(Secret, douban!.ToString());

            transport.Answer("/media/85?", nativeBangumi).Fail(SiteDetail, HttpStatusCode.ServiceUnavailable);
            var bangumi = fixture.Resolve(ids, season: 1, includeBangumi: true);
            Assert.Equal(85L, bangumi?.BangumiId);
            Assert.True(bangumi?.DoubanLookupFailed == true);
            Assert.False(bangumi?.BangumiLookupFailed == true);
        });
        Test("MP 打格子：可选站点读取遇调用方取消时终止匹配", () =>
        {
            using var transport = new StubTransport().Answer(SeriesDetail, Series).Answer(EpisodePath, Episode)
                .Answer(SiteDetail, Site).Answer(MappingPath, Mapping);
            using var fixture = new ConnectedFixture(transport);
            using var cancellation = new CancellationTokenSource();
            transport.When(SiteDetail, cancellation.Cancel);
            var ids = new Dictionary<string, string> { ["Douban"] = "84", ["Tmdb"] = "42" };
            Assert.Throws<OperationCanceledException>(() => fixture.Resolve(ids, includeBangumi: true, cancellationToken: cancellation.Token));
            Assert.Equal(0, transport.Count(MappingPath));
        });
        Test("MP 打格子：已有站点编号及明确第一季，老版本缺共享接口仍可核验直给篇目", () =>
        {
            using var transport = new StubTransport().Answer(SeriesDetail, Series).Answer("/tmdb/42/1", Episode.Replace("\"season_number\":2", "\"season_number\":1", StringComparison.Ordinal))
                .Answer(SiteDetail, Site).Fail(MappingPath, HttpStatusCode.NotImplemented);
            using var fixture = new ConnectedFixture(transport);
            var result = fixture.Resolve(new Dictionary<string, string> { ["Tmdb"] = "42", ["Douban"] = "84" }, season: 1);
            Assert.Equal(84L, result?.DoubanId);
            Assert.Null(result?.DoubanEpisode);
        });
        Test("MP 打格子：来源编号冲突、非法路径、缺失年份、未连接均拦截读请求", () =>
        {
            using var transport = new StubTransport();
            using var fixture = new ConnectedFixture(transport, connect: false);
            Assert.Null(fixture.Resolve());
            Assert.Equal("", fixture.Service.ScrobblingConnectionStamp);
            Assert.Equal(0, transport.Total);
            fixture.Connect();
            Assert.Null(fixture.Resolve(new Dictionary<string, string> { ["Tmdb"] = "42", ["tmdb"] = "43" }));
            Assert.Null(fixture.Resolve(new Dictionary<string, string> { ["Tmdb"] = @"C:\private\data" }));
            Assert.Null(fixture.Resolve(new Dictionary<string, string>(), title: @"C:\private\data", originalTitle: null));
            Assert.Null(fixture.Resolve(new Dictionary<string, string>(), year: null));
            Assert.Equal(2, transport.Total);
        });
        Test("MP 打格子：在途请求遇 A→B→A 同配置回切，旧结果和旧戳均不得恢复", () =>
        {
            using var transport = new StubTransport().Answer(SeriesDetail, Series).Answer(EpisodePath, Episode);
            using var fixture = new ConnectedFixture(transport);
            var stamp = fixture.Service.ScrobblingConnectionStamp;
            transport.When(SeriesDetail, () =>
            {
                fixture.Settings.MoviePilot.Url = "https://another.invalid:3001";
                fixture.Settings.MoviePilot.Url = "https://fixture.invalid:3001";
            });
            Assert.Throws<OperationCanceledException>(() => fixture.Resolve());
            Assert.Equal(0, transport.Count(EpisodePath));
            fixture.Connect();
            Assert.False(fixture.Service.IsCurrentScrobblingConnection(stamp));
            Assert.True(fixture.Service.ScrobblingConnectionStamp.Length > 0);
        });
        Test("MP 打格子：季集连续、无重号且资料总集数一致时提供季末", () =>
        {
            var series = Series.Replace("\"year\":2024",
                "\"year\":2024,\"season_info\":[{\"season_number\":2,\"episode_count\":10}]", StringComparison.Ordinal);
            var season = Enumerable.Range(1, 10).Reverse().Select(number => FormattableString.Invariant(
                $"{{\"id\":{700 + number},\"show_id\":42,\"season_number\":2,\"episode_number\":{number}}}"));
            var consecutive = "[" + string.Join(',', season) + "]";
            using var transport = new StubTransport().Answer(SeriesDetail, series).Answer(EpisodePath, consecutive).Answer(SearchPath, "[]");
            using var fixture = new ConnectedFixture(transport);
            Assert.Equal(10, fixture.Resolve()?.TmdbSeasonLastEpisode);

            transport.Answer(EpisodePath, consecutive.Replace("\"episode_number\":8", "\"episode_number\":9", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve()?.TmdbSeasonLastEpisode);

            transport.Answer(EpisodePath, consecutive);
            transport.Answer(SeriesDetail, series.Replace("\"episode_count\":10", "\"episode_count\":11", StringComparison.Ordinal));
            Assert.Null(fixture.Resolve()?.TmdbSeasonLastEpisode);
        });
        Test("MP 打格子：上游回显密钥时只将本地错误及状态交给调用方", () =>
        {
            using var transport = new StubTransport().Sequence(SeriesDetail, (HttpStatusCode.Forbidden, "{\"message\":\"" + Secret + "\"}"));
            using var fixture = new ConnectedFixture(transport);
            var error = Assert.Catch<MoviePilotException>(() => fixture.Resolve());
            Assert.Equal(HttpStatusCode.Forbidden, error.Status);
            Assert.DoesNotContain(Secret, error.ToString());
            Assert.DoesNotContain(Secret, transport.Only(SeriesDetail).Url);
            Assert.Null(error.Body);
            Assert.Null(error.InnerException);
        });
        Test("MP 打格子：只读识别响应超过大小上限即停止", () =>
        {
            using var transport = new StubTransport().Understate(SeriesDetail, Series, 9 * 1024 * 1024);
            using var fixture = new ConnectedFixture(transport);
            Assert.Throws<MoviePilotException>(() => fixture.Resolve());
            Assert.Equal(0, transport.SentTo(EpisodePath).Count);
        });
    }

    private sealed class ConnectedFixture : IDisposable
    {
        private readonly MoviePilotClient _client;
        internal AppSettings Settings { get; } = new();
        internal MoviePilotService Service { get; }

        internal ConnectedFixture(StubTransport transport, bool connect = true)
        {
            Settings.MoviePilot.Enabled = true;
            Settings.MoviePilot.Url = "https://fixture.invalid:3001";
            Settings.MoviePilot.Username = "fixture";
            var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
            credentials.SetPassword(Settings.MoviePilot, Secret);
            transport.Answer("/login/access-token", "{\"access_token\":\"" + Secret + "\"}");
            transport.Answer("/dashboard/system", "{}");
            _client = new MoviePilotClient(transport);
            Service = new MoviePilotService(_client, credentials, Settings);
            if (connect) Connect();
        }

        internal void Connect() => Assert.True(Service.RefreshAvailabilityAsync().GetAwaiter().GetResult());

        internal MoviePilotScrobblingMatch? Resolve(IReadOnlyDictionary<string, string>? ids = null, int season = 2,
            string title = "示例剧", string? originalTitle = "Fixture Show", int? year = 2024, int episode = 10,
            bool includeBangumi = false, bool includeDouban = true, CancellationToken cancellationToken = default) =>
            Service.ResolveScrobblingEpisodeAsync(title, originalTitle, year,
                ids ?? new Dictionary<string, string> { ["Tmdb"] = "42" }, season, episode,
                includeBangumi, includeDouban, cancellationToken).GetAwaiter().GetResult();

        public void Dispose()
        {
            Service.Dispose();
            _client.Dispose();
        }
    }
}
