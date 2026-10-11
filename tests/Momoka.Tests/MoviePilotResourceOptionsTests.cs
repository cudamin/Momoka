using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotResourceOptionsTests
{
    public static void Register()
    {
        Test("MoviePilot 资源折叠：同发布跨站点保留原对象、优惠与下载身份", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"Show S01E03 1080p-GROUP","size":1024,"site_name":"甲","downloadvolumefactor":0}},
                 {"torrent_info":{"title":" show  S01E03 1080p-GROUP ","size":1024,"site_name":"乙","downloadvolumefactor":1}},
                 {"torrent_info":{"title":"Show S01E03 1080p-GROUP","size":2048,"site_name":"丙"}},
                 {"torrent_info":{"title":"Show S01E03 1080p-OTHER","size":1024,"site_name":"丁"}}]
                """);
            var groups = MoviePilotResourceGrouping.Group(rows);
            Assert.Equal(3, groups.Count);
            Assert.Equal(2, groups[0].Count);
            Assert.True(ReferenceEquals(rows[1], groups[0][1]));
            Assert.True(groups[0][0].IsFree);
            Assert.False(groups[0][1].IsFree);
            var filtered = new MoviePilotResourceFilter { Site = "乙" }.Apply(rows, DateTimeOffset.Now);
            Assert.Equal("乙", MoviePilotResourceGrouping.Group(filtered).Single().Single().SiteName);
        });
        Test("MoviePilot 资源折叠：未知体积和冲突媒体不能按同名合并", () =>
        {
            var resource = Parse("""[{"torrent_info":{"title":"Show"}}]""")[0];
            Assert.Equal(2, MoviePilotResourceGrouping.Group([resource, resource with { SiteName = "乙" }]).Count);
            Assert.Equal(2, MoviePilotResourceGrouping.Group([
                resource with { Size = 100, MediaSource = "douban", MediaId = "1" },
                resource with { Size = 100, MediaSource = "themoviedb", MediaId = "1" }]).Count);
        });
        Test("MoviePilot 季集筛选：特别篇、范围、离散集和未知编号", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"特别篇"},"meta_info":{"begin_season":0,"begin_episode":1,"end_episode":3}},
                 {"torrent_info":{"title":"双季合集"},"meta_info":{"begin_season":"1","end_season":2,"episode_list":[1,3,"5"],"begin_episode":1,"end_episode":5}},
                 {"torrent_info":{"title":"第二季"},"meta_info":{"begin_season":2}},
                 {"torrent_info":{"title":"未知"}}]
                """);
            Assert.Equal(1, new MoviePilotResourceFilter { Season = 0, Episode = 2 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(1, new MoviePilotResourceFilter { Season = 2, Episode = 3 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(0, new MoviePilotResourceFilter { Season = 2, Episode = 2 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(2, new MoviePilotResourceFilter { Season = 2 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(4, new MoviePilotResourceFilter().Apply(rows, DateTimeOffset.Now).Count());
        });
        Test("MoviePilot 季集筛选：畸形和超大范围不展开", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"资源"},"meta_info":{"begin_season":2,"end_season":1,"begin_episode":1,"end_episode":2147483647}}]
                """);
            Assert.Equal(0, rows[0].Seasons.Count);
            Assert.Equal(0, rows[0].Episodes.Count);
        });
        Test("MoviePilot 下载选项：目录只读取名称及保存路径，远程路径不丢存储前缀", () =>
        {
            using var transport = Transport()
                .Answer("download/clients", """[{"name":"下载器甲","type":"qbittorrent","password":"private"},{"name":"下载器甲"},{"name":""}]""")
                .Answer("download/paths", """[{"name":"动画","save_path":"rclone:/media/anime","download_path":"/media/anime"},{"save_path":"/downloads"}]""");
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var choices = service.DownloadChoicesAsync(resource, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, choices.Downloaders.Count);
            Assert.Equal("rclone:/media/anime", choices.Paths[0]);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(choices));
            Assert.Equal("GET", transport.Only("download/clients").Method);
            Assert.Equal("GET", transport.Only("download/paths").Method);
        });
        Test("MoviePilot 下载选项：默认省略字段，指定选项回传且不能换下载器重复提交", () =>
        {
            foreach (var complete in new[] { false, true })
            {
                using var transport = Transport().Answer("download/", """{"success":true}""");
                var (service, _) = Service(transport);
                var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
                if (complete) resource = resource with { MediaInfo = Json("""{"title":"测试","type":"电视剧","media_source":"themoviedb","media_id":"42"}""") };
                var defaults = MoviePilotMediaParser.DownloadBody(resource);
                Assert.False(defaults.ContainsKey("downloader"));
                Assert.False(defaults.ContainsKey("save_path"));
                service.DownloadAsync(resource, CancellationToken.None, new("下载器乙", "rclone:/media/动画")).GetAwaiter().GetResult();
                var sent = transport.Only("download/");
                Assert.True(sent.Url.EndsWith(complete ? "download/" : "download/add", StringComparison.Ordinal));
                using var body = JsonDocument.Parse(sent.Body);
                Assert.Equal("下载器乙", body.RootElement.GetProperty("downloader").GetString());
                Assert.Equal("rclone:/media/动画", body.RootElement.GetProperty("save_path").GetString());
                Assert.Equal("资源", body.RootElement.GetProperty("torrent_in").GetProperty("title").GetString());
                Assert.Equal(complete, body.RootElement.TryGetProperty("media_in", out _));
                Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource, CancellationToken.None, new("另一个", "/elsewhere")).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Count("download/"));
            }
        });
        Test("MoviePilot 下载选项：切换连接后旧资源不得读取目录或提交", () =>
        {
            using var transport = Transport();
            var (service, settings) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            settings.MoviePilot.Username = "另一个账号";
            var before = transport.Total;
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadChoicesAsync(resource, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource, CancellationToken.None, new("下载器", "/downloads")).GetAwaiter().GetResult());
            Assert.Equal(before, transport.Total);
        });
    }

    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static IReadOnlyList<MoviePilotResource> Parse(string json) => MoviePilotMediaParser.ParseResources(Json(json), null, null);
    private static StubTransport Transport() => new StubTransport()
        .Answer("login/access-token", """{"access_token":"fixture","super_user":true}""")
        .Answer("search/title", """[{"torrent_info":{"title":"资源","size":100,"site":1,"page_url":"https://site.invalid/details/1"}}]""");
    private static (MoviePilotService Service, AppSettings Settings) Service(StubTransport transport)
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(settings.MoviePilot, "fixture");
        return (new MoviePilotService(new MoviePilotClient(transport), credentials, settings), settings);
    }
}
