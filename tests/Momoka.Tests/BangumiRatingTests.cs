using System.Net;
using System.Text;
using Momoka.Bangumi;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class BangumiRatingTests
{
    private const long RequestedSubject = 123;
    private const string ExactUrl = "https://api.bgm.tv/v0/subjects/123";

    internal static void Register()
    {
        RegisterSearchTests();
        Test("Bangumi 评分读取明确的作品、剧或季编号，拒绝借用父剧和单集编号", () =>
        {
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season })
            {
                var item = new EmbyItem { Type = type, ProviderIds = new() { ["bAnGuMi"] = " 123 ", ["BgM"] = "0123", ["Tmdb"] = "999" } };
                Assert.Equal<long?>(RequestedSubject, BangumiRatingIdentity.For(item));
            }
            var season = new EmbyItem { Type = EmbyItemType.Season, SeriesId = "parent", ProviderIds = new() { ["Tmdb"] = "123" } };
            Assert.Null(BangumiRatingIdentity.For(season));
            season.ProviderIds["Bgm"] = "456";
            Assert.Equal<long?>(456, BangumiRatingIdentity.For(season));
            Assert.Null(BangumiRatingIdentity.For(new EmbyItem { Type = EmbyItemType.Episode, ProviderIds = season.ProviderIds }));
        });
        Test("Bangumi 评分拒绝冲突和非法编号", () =>
        {
            var item = new EmbyItem { Type = EmbyItemType.Series, ProviderIds = new() { ["Bangumi"] = "123" } };
            foreach (var value in new[] { "456", "", "0", "10000001", "-123", "+123", "https://bgm.tv/subject/123" })
            {
                item.ProviderIds["Bgm"] = value;
                Assert.Null(BangumiRatingIdentity.For(item));
            }
        });
        Test("Bangumi 官方请求匿名携带应用 User-Agent，真实五分和满分保留", () =>
        {
            using var transport = new Transport((_, _) => Json(Reply("8.65")));
            using var client = new BangumiClient(transport);
            Assert.Equal<decimal?>(8.65m, Read(client));
            Assert.Equal(ExactUrl, transport.Requests.Single());
            Assert.Equal<decimal?>(5m, ReadScore("5.0"));
            Assert.Equal<decimal?>(10m, ReadScore("10"));
            using var realTransport = new Transport((_, _) => Json(Reply("8", type: "6")));
            using var realClient = new BangumiClient(realTransport);
            Assert.Equal<decimal?>(8m, Read(realClient));
        });
        Test("Bangumi 评分人数为零或字段不明时隐藏，拒绝错误编号和条目类型", () =>
        {
            foreach (var response in new[]
            {
                Reply("0"), Reply("5", total: "0"), Reply("5", total: "null"), Reply("5", total: "\"1\""),
                Reply("null"), Reply("\"8.5\""), Reply("-0.1"), Reply("10.1"), Reply("5", id: 456),
                Reply("5", type: "1"), Reply("5", type: "3"), Reply("5", type: "null"),
                "{}", "[]", "{", """{"id":"123","type":2,"rating":{"score":5,"total":1}}""",
                """{"id":123,"type":2,"rating":null}""", """{"id":123,"type":2,"rating":{"score":5}}"""
            })
            {
                using var transport = new Transport((_, _) => Json(response));
                using var client = new BangumiClient(transport);
                Assert.Null(Read(client));
            }
        });
        Test("Bangumi 合并条目只跟随官方同源编号跳转，循环和外站均隐藏", () =>
        {
            using var canonical = new Transport((uri, _) => uri.AbsoluteUri == ExactUrl
                ? Redirect("/v0/subjects/456") : Json(Reply("7.5", id: 456)));
            using var client = new BangumiClient(canonical);
            Assert.Equal<decimal?>(7.5m, Read(client));
            Assert.Equal(2, canonical.Requests.Count);
            foreach (var location in new[] { "https://other.invalid/v0/subjects/123", "http://api.bgm.tv/v0/subjects/123",
                "/v0/subjects/123?token=placeholder", "/v0/subjects/123#details", "/subjects/123", "/v0/subjects/123/" })
            {
                using var transport = new Transport((_, _) => Redirect(location));
                using var denied = new BangumiClient(transport);
                Assert.Null(Read(denied));
                Assert.Equal(1, transport.Requests.Count);
            }
            using var loop = new Transport((_, _) => Redirect("/v0/subjects/123"));
            using var loopingClient = new BangumiClient(loop);
            Assert.Null(Read(loopingClient));
            Assert.Equal(4, loop.Requests.Count);
        });
        Test("Bangumi 错误状态和超限正文只隐藏评分，不读取服务端错误正文", () =>
        {
            foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
            {
                using var transport = new Transport((_, _) => new(status));
                using var client = new BangumiClient(transport);
                Assert.Null(Read(client));
            }
            using var oversized = new Transport((_, _) => Json(new string(' ', 1024 * 1024 + 1)));
            using var oversizedClient = new BangumiClient(oversized);
            Assert.Null(Read(oversizedClient));
            using var failed = new Transport((_, _) => throw new HttpRequestException("fixture"));
            using var failedClient = new BangumiClient(failed);
            Assert.Null(Read(failedClient));
        });
        Test("Bangumi 主动取消中止在途请求，无效编号不访问网络", () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var transport = new Transport((_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("在途请求未收到取消");
            });
            using var client = new BangumiClient(transport);
            Assert.Throws<ArgumentOutOfRangeException>(() => client.RatingAsync(0, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => client.RatingAsync(10000001, default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests.Count);
            Assert.Throws<OperationCanceledException>(() => client.RatingAsync(RequestedSubject, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
            Assert.Throws<OperationCanceledException>(() => client.RatingAsync(RequestedSubject, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
        });
    }

    private static decimal? Read(BangumiClient client) => client.RatingAsync(RequestedSubject, default).GetAwaiter().GetResult();

    private static decimal? ReadScore(string value)
    {
        using var transport = new Transport((_, _) => Json(Reply(value)));
        using var client = new BangumiClient(transport);
        return Read(client);
    }

    private static string Reply(string score, string total = "1", int id = 123, string type = "2") =>
        $$$"""{"id":{{{id}}},"type":{{{type}}},"rating":{"score":{{{score}}},"total":{{{total}}}}}""";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Redirect(string target) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(target, UriKind.RelativeOrAbsolute) } };

    private sealed class Transport(Func<Uri, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Contains("Momoka/", request.Headers.UserAgent.ToString());
            Assert.Contains("https://github.com/cudamin/Momoka", request.Headers.UserAgent.ToString());
            var uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult(respond(uri, cancellationToken));
        }
    }
}
