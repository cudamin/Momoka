using System.Net;
using System.Text;
using Momoka.AniBt;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtRatingTests
{
    private const long RequestedSubject = 123;
    private const string ExactUrl = "https://anibt.net/api/seasons/anime?bgmId=123";
    private const string Scored = """{"animeId":"sample","bgmId":123,"rating":8.65}""";

    internal static void Register()
    {
        Test("AniBT 参考分读取明确的作品、剧或季编号", () =>
        {
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season })
            {
                var item = new EmbyItem { Type = type, ProviderIds = new() { ["bAnGuMi"] = " 123 ", ["BgM"] = "0123", ["Tmdb"] = "999" } };
                Assert.Equal<long?>(RequestedSubject, AniBtRatingIdentity.For(item));
            }

            var season = new EmbyItem { Type = EmbyItemType.Season, SeriesId = "parent", IndexNumber = 2,
                Name = "作品第二季", ProviderIds = new() { ["Tmdb"] = "123", ["Imdb"] = "tt123" } };
            Assert.Null(AniBtRatingIdentity.For(season));
            season.ProviderIds["Bgm"] = "456";
            Assert.Equal<long?>(456, AniBtRatingIdentity.For(season));
            Assert.Null(AniBtRatingIdentity.For(new EmbyItem { Type = EmbyItemType.Episode, ProviderIds = season.ProviderIds }));
        });

        Test("AniBT 参考分拒绝不明、冲突及非法编号", () =>
        {
            var item = new EmbyItem { Type = EmbyItemType.Series, ProviderIds = new() { ["Bangumi"] = "123" } };
            foreach (var value in new[] { "456", "", "0", "10000001", "-123", "+123", "https://bgm.tv/subject/123" })
            {
                item.ProviderIds["Bgm"] = value;
                Assert.Null(AniBtRatingIdentity.For(item));
            }
        });

        Test("AniBT 参考分按确切编号匿名查询，保留十分制有效分和零分", () =>
        {
            using var transport = new Transport((_, _) => Json(Reply(Scored)));
            using var client = new AniBtClient(transport);
            Assert.Equal<decimal?>(8.65m, Read(client));
            Assert.Equal(1, transport.Requests.Count);
            Assert.Equal(ExactUrl, transport.Requests[0]);
            Assert.Equal<decimal?>(0m, ReadScore("0"));
            Assert.Equal<decimal?>(10m, ReadScore("10"));
        });

        Test("AniBT 参考分隐藏五分占位、缺失、字符串和超出十分制的值", () =>
        {
            foreach (var value in new[] { "5", "5.0", "null", "\"8.5\"", "-0.1", "10.1" })
                Assert.Null(ReadScore(value));
            using var transport = new Transport((_, _) => Json(Reply("""{"animeId":"sample","bgmId":123}""")));
            using var client = new AniBtClient(transport);
            Assert.Null(Read(client));
        });

        Test("AniBT 参考分只接受确切查询下完整的唯一结果", () =>
        {
            foreach (var response in new[]
            {
                Reply(Scored, scopeKind: "season"), Reply(Scored, scopeId: 456), Reply(Scored, total: 2),
                Reply(Scored, truncated: "true"), Reply(""), Reply(Scored + "," + Scored, total: 1),
                Reply("""{"animeId":"","bgmId":123,"rating":8.65}"""),
                Reply("""{"animeId":"sample","bgmId":null,"rating":8.65}"""),
                """{"ok":true,"data":{"scope":{"kind":"bgmId","bgmId":123},"total":1,"truncated":false,"byWeekday":[{"animes":null}]}}""",
                """{"ok":false,"data":{}}"""
            })
            {
                using var transport = new Transport((_, _) => Json(response));
                using var client = new AniBtClient(transport);
                Assert.Null(Read(client));
            }

            // AniBT can bless a canonical result for a merged subject while echoing the precise requested ID.
            var canonical = Scored.Replace("123", "456", StringComparison.Ordinal);
            using var merged = new Transport((_, _) => Json(Reply(canonical)));
            using var mergedClient = new AniBtClient(merged);
            Assert.Equal<decimal?>(8.65m, Read(mergedClient));
        });

        Test("AniBT 参考分拒绝跳转到别的编号、路径、站点或额外查询条件", () =>
        {
            foreach (var location in new[] { "/api/seasons/anime?bgmId=456", "/api/seasons/anime?bgmId=123&bgmId=456",
                         "/api/public/v1/anime?bgmId=123", "https://redirect.invalid/api/seasons/anime?bgmId=123" })
            {
                using var transport = new Transport((_, _) => new(HttpStatusCode.TemporaryRedirect)
                    { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } });
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => Read(client));
                Assert.Equal(1, transport.Requests.Count);
            }
        });

        Test("AniBT 参考分主动取消中止在途请求，无效编号不访问网络", () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var transport = new Transport((_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("在途请求未收到取消");
            });
            using var client = new AniBtClient(transport);
            Assert.Throws<ArgumentOutOfRangeException>(() => client.ReferenceRatingAsync(0, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => client.ReferenceRatingAsync(10000001, default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests.Count);
            Assert.Throws<OperationCanceledException>(() => client.ReferenceRatingAsync(RequestedSubject, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
            Assert.Throws<OperationCanceledException>(() => client.ReferenceRatingAsync(RequestedSubject, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
        });
    }

    private static decimal? Read(AniBtClient client) => client.ReferenceRatingAsync(RequestedSubject, default).GetAwaiter().GetResult();

    private static decimal? ReadScore(string value)
    {
        var subject = $$"""{"animeId":"sample","bgmId":123,"rating":{{value}}}""";
        using var transport = new Transport((_, _) => Json(Reply(subject)));
        using var client = new AniBtClient(transport);
        return Read(client);
    }

    private static string Reply(string entries, string scopeKind = "bgmId", int scopeId = 123, int total = 1, string truncated = "false") =>
        $$$"""{"ok":true,"data":{"scope":{"kind":"{{{scopeKind}}}","bgmId":{{{scopeId}}}},"total":{{{total}}},"truncated":{{{truncated}}},"byWeekday":[{"animes":[{{{entries}}}]}]}}""";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Transport(Func<Uri, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            var uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult(respond(uri, cancellationToken));
        }
    }
}
