using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotSearchSitesTests
{
    public static void Register()
    {
        Test("MoviePilot 搜索站点：目录按媒体类型读取，只投影启用站点的编号和名称", () =>
        {
            foreach (var (type, path) in new[] { ((string?)null, "site/"), ("电影", "site/media/movie"), ("电视剧", "site/media/tv") })
            {
                using var transport = Transport().Answer("site/", """
                    [{"id":1,"name":"站点甲","is_active":true,"cookie":"private","url":"https://private.invalid"},
                     {"id":"2","name":"站点乙","is_active":true},{"id":3,"name":"关闭","is_active":false},
                     {"id":1,"name":"重复","is_active":true},{"id":0,"name":"无效","is_active":true}]
                    """);
                var (service, _) = Service(transport);
                var sites = service.SearchSitesAsync(type, CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(2, sites.Count);
                Assert.Equal(2, sites[1].Id);
                Assert.True(transport.Only("site/").Url.EndsWith(path, StringComparison.Ordinal));
                Assert.DoesNotContain("private", JsonSerializer.Serialize(sites));
                Assert.DoesNotContain("cookie", JsonSerializer.Serialize(sites));
            }
        });
        Test("MoviePilot 搜索站点：关键词和精确资源均发送单个逗号分隔 sites 参数", () =>
        {
            using var transport = Transport().Answer("search/title", "[]").Answer("search/media", "[]");
            var (service, _) = Service(transport);
            var selection = new MoviePilotSiteSelection(service.ConnectionStamp, [2, 9, 2]);
            service.SearchByKeywordAsync("测试 & 中文", CancellationToken.None, selection).GetAwaiter().GetResult();
            service.SearchResourcesAsync(new MoviePilotMedia { Title = "测试", Type = "电视剧", MediaSource = "themoviedb", MediaId = "42", Season = 0 },
                CancellationToken.None, selection).GetAwaiter().GetResult();
            foreach (var path in new[] { "search/title", "search/media" })
            {
                var uri = new Uri(transport.Only(path).Url);
                var pairs = uri.Query.TrimStart('?').Split('&');
                Assert.Equal(1, pairs.Count(pair => pair.StartsWith("sites=", StringComparison.Ordinal)));
                Assert.Equal("sites=2,9", Uri.UnescapeDataString(pairs.Single(pair => pair.StartsWith("sites=", StringComparison.Ordinal))));
            }
            Assert.Contains("season=0", transport.Only("search/media").Url);
            Assert.Contains("media_source=themoviedb", transport.Only("search/media").Url);
        });
        Test("MoviePilot 搜索站点：默认省略参数，明确全选仍发送具体编号", () =>
        {
            using var transport = Transport().Answer("search/title", "[]");
            var (service, _) = Service(transport);
            service.SearchByKeywordAsync("词", CancellationToken.None).GetAwaiter().GetResult();
            Assert.DoesNotContain("sites=", transport.Only("search/title").Url);
            service.SearchByKeywordAsync("词", CancellationToken.None, new MoviePilotSiteSelection(service.ConnectionStamp, [1, 2])).GetAwaiter().GetResult();
            Assert.Contains("sites=1,2", Uri.UnescapeDataString(transport.SentTo("search/title")[1].Url));
        });
        Test("MoviePilot 搜索站点：清空和非法编号不退回默认搜索，不发网络请求", () =>
        {
            foreach (var ids in new int[][] { [], [0], [-1], [1, -2] })
            {
                using var transport = Transport();
                var (service, _) = Service(transport);
                Assert.Throws<MoviePilotException>(() => service.SearchByKeywordAsync("词", CancellationToken.None,
                    new MoviePilotSiteSelection(service.ConnectionStamp, ids)).GetAwaiter().GetResult());
                Assert.Equal(0, transport.Total);
            }
        });
        Test("MoviePilot 搜索站点：连接改变后拒绝旧站点编号", () =>
        {
            using var transport = Transport();
            var (service, settings) = Service(transport);
            var selected = new MoviePilotSiteSelection(service.ConnectionStamp, [1]);
            settings.MoviePilot.Username = "another-user";
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.SearchByKeywordAsync("词", CancellationToken.None, selected).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Total);
        });
    }

    private static StubTransport Transport() => new StubTransport().Answer("login/access-token", """{"access_token":"fixture-token","super_user":true}""");

    private static (MoviePilotService Service, AppSettings Settings) Service(StubTransport transport)
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(settings.MoviePilot, "fixture-password");
        return (new MoviePilotService(new MoviePilotClient(transport), credentials, settings), settings);
    }
}
