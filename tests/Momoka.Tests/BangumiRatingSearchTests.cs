using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Bangumi;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class BangumiRatingTests
{
    private static void RegisterSearchTests()
    {
        Test("Bangumi 缺少编号时按中文、原文和官方别名匹配截图作品，读取官方十分制原分", () =>
        {
            foreach (var sample in new[]
            {
                ("米奇与达利", "ミギとダリ", "米奇与达利", "Migi & Dali", 2023, "TV", EmbyItemType.Series, 6.7m),
                ("记忆中的玛妮", "思い出のマーニー", "回忆中的玛妮", "记忆中的玛妮", 2014, "剧场版", EmbyItemType.Movie, 7.2m)
            })
            {
                var item = SearchItem(sample.Item1, sample.Item5, sample.Item7);
                using var transport = new SearchTransport((_, _) => SearchReply(Candidate(sample.Item2, sample.Item3,
                    sample.Item4, sample.Item5, sample.Item6)), sample.Item8);
                using var client = new BangumiClient(transport);
                Assert.Equal<decimal?>(sample.Item8, client.RatingAsync(item, default).GetAwaiter().GetResult());
                Assert.Equal(2, transport.Paths.Count);
                Assert.Equal("/v0/search/subjects?limit=20", transport.Paths[0]);
                Assert.Equal("/v0/subjects/123", transport.Paths[1]);
                using var body = JsonDocument.Parse(transport.Bodies.Single());
                Assert.Equal(item.Name, body.RootElement.GetProperty("keyword").GetString());
                var filter = body.RootElement.GetProperty("filter");
                Assert.Equal($">={sample.Item5}-01-01", filter.GetProperty("air_date")[0].GetString());
                Assert.Equal($"<{sample.Item5 + 1}-01-01", filter.GetProperty("air_date")[1].GetString());
                Assert.Equal(2, filter.GetProperty("type")[0].GetInt32());
                Assert.Equal(6, filter.GetProperty("type")[1].GetInt32());
            }
        });
        Test("Bangumi 官方搜索先查原文，中文回退可用，全半角和标点差异可匹配", () =>
        {
            var item = SearchItem("Migi & Dali", 2023, EmbyItemType.Series);
            item.OriginalTitle = "unknown translation";
            using var transport = new SearchTransport((_, call) => call == 1 ? SearchReply()
                : SearchReply(Candidate("Ｍｉｇｉ ＆ Ｄａｌｉ", "米奇与达利", "", 2023, "TV")));
            using var client = new BangumiClient(transport);
            Assert.Equal<decimal?>(8m, client.RatingAsync(item, default).GetAwaiter().GetResult());
            Assert.Equal(3, transport.Paths.Count);
            using var first = JsonDocument.Parse(transport.Bodies[0]);
            using var second = JsonDocument.Parse(transport.Bodies[1]);
            Assert.Equal(item.OriginalTitle, first.RootElement.GetProperty("keyword").GetString());
            Assert.Equal(item.Name, second.RootElement.GetProperty("keyword").GetString());
        });
        Test("Bangumi 官方搜索拒绝年份、影视形态和标题冲突，以及同名多条目", () =>
        {
            var item = SearchItem("Wanted", 2023, EmbyItemType.Series);
            foreach (var response in new[]
            {
                SearchReply(Candidate("Wanted", "", "", 2022, "TV")),
                SearchReply(Candidate("Wanted", "", "", 2023, "剧场版")),
                SearchReply(Candidate("Wanted sequel", "", "", 2023, "TV")),
                SearchReply(Candidate("Wanted", "", "", 2023, "其他")),
                SearchReply(Candidate("Wanted", "", "", 2023, "TV"), Candidate("Wanted", "", "", 2023, "TV", id: 456))
            })
            {
                using var transport = new SearchTransport((_, _) => response);
                using var client = new BangumiClient(transport);
                Assert.Null(client.RatingAsync(item, default).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Paths.Count);
            }
        });
        Test("Bangumi 已有编号优先，冲突编号、通用季名、单集和未知年份不触发标题搜索", () =>
        {
            using var transport = new SearchTransport((_, _) => throw new AssertionException("不应搜索"));
            using var client = new BangumiClient(transport);
            var item = SearchItem("Wanted", 2023, EmbyItemType.Movie);
            item.ProviderIds["Bangumi"] = "123";
            Assert.Equal<decimal?>(8m, client.RatingAsync(item, default).GetAwaiter().GetResult());
            foreach (var value in new[] { "0", "invalid", "456" })
            {
                item.ProviderIds["Bgm"] = value;
                Assert.Null(client.RatingAsync(item, default).GetAwaiter().GetResult());
            }
            item.ProviderIds.Clear();
            foreach (var type in new[] { EmbyItemType.Season, EmbyItemType.Episode })
            {
                item.Type = type;
                Assert.Null(client.RatingAsync(item, default).GetAwaiter().GetResult());
            }
            item.Type = EmbyItemType.Movie;
            item.ProductionYear = null;
            Assert.Null(client.RatingAsync(item, default).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Paths.Count);
        });
        Test("Bangumi 搜索正文损坏或超限时隐藏，搜索返回的分数不能绕过评分人数核验", () =>
        {
            var item = SearchItem("Wanted", 2023, EmbyItemType.Series);
            foreach (var response in new[] { "{}", "[]", "{", "{\"total\":\"1\",\"data\":[]}",
                "{\"total\":21,\"data\":[]}", new string(' ', 1024 * 1024 + 1), SearchReply("null") })
            {
                using var transport = new SearchTransport((_, _) => response);
                using var client = new BangumiClient(transport);
                Assert.Null(client.RatingAsync(item, default).GetAwaiter().GetResult());
            }
            using var noVotes = new SearchTransport((_, _) => SearchReply(Candidate("Wanted", "", "", 2023, "TV")), total: 0);
            using var noVotesClient = new BangumiClient(noVotes);
            Assert.Null(noVotesClient.RatingAsync(item, default).GetAwaiter().GetResult());
            Assert.Equal(2, noVotes.Paths.Count);
        });
        Test("Bangumi 官方搜索支持首播年份和真人电影，离页取消贯穿搜索与评分请求", () =>
        {
            var item = SearchItem("Wanted", 2023, EmbyItemType.Movie);
            item.ProductionYear = null;
            item.PremiereDate = DateTimeOffset.Parse("2023-06-01T00:00:00Z");
            using var transport = new SearchTransport((_, _) => SearchReply(Candidate("Wanted", "", "", 2023, "电影", type: 6)));
            using var client = new BangumiClient(transport);
            Assert.Equal<decimal?>(8m, client.RatingAsync(item, default).GetAwaiter().GetResult());
            using var cancellation = new CancellationTokenSource();
            using var canceled = new SearchTransport((token, _) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new AssertionException("取消未传入搜索");
            });
            using var canceledClient = new BangumiClient(canceled);
            Assert.Throws<OperationCanceledException>(() => canceledClient.RatingAsync(item, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, canceled.Paths.Count);
        });
    }

    private static EmbyItem SearchItem(string title, int year, string type) => new() { Name = title, ProductionYear = year, Type = type };

    private static string Candidate(string name, string nameCn, string alias, int year, string platform, int id = 123, int type = 2) =>
        JsonSerializer.Serialize(new
        {
            id,
            type,
            name,
            name_cn = nameCn,
            date = $"{year}-07-19",
            platform,
            infobox = new[] { new { key = "别名", value = new[] { new { v = alias } } } }
        });

    private static string SearchReply(params string[] candidates) =>
        "{\"total\":" + candidates.Length + ",\"data\":[" + string.Join(',', candidates) + "]}";

    private sealed class SearchTransport(Func<CancellationToken, int, string> search, decimal score = 8m, int total = 1) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Contains("Momoka/", request.Headers.UserAgent.ToString());
            var uri = request.RequestUri!;
            Assert.Equal("api.bgm.tv", uri.Host);
            Paths.Add(uri.PathAndQuery);
            string body;
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/v0/search/subjects?limit=20", uri.PathAndQuery);
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                body = search(cancellationToken, Bodies.Count);
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/v0/subjects/123", uri.AbsolutePath);
                body = JsonSerializer.Serialize(new { id = 123, type = 2, rating = new { score, total } });
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
