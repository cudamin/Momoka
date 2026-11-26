using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Scrobbling;

namespace Momoka.Tests;

internal static class ScrobblingBangumiTests
{
    private const string Token = "synthetic-bangumi-access";

    public static void Register()
    {
        Case("Bangumi：已收藏且单话看过时不改任何收藏", async () =>
        {
            using var transport = Existing(2, 2);
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            await client.MarkEpisodeAsync(8, 10, Token, () => true, CancellationToken.None);
            Assert.Equal(3, transport.Sent.Count);
            Assert.True(transport.Sent.All(request => request.Method == HttpMethod.Get));
            transport.AssertComplete();
        });

        Case("Bangumi：用站点接受的 JSON 请求头创建在看，再只标目标单话", async () =>
        {
            using var transport = new Transport(
                Reply(HttpMethod.Get, "/v0/me", Me()),
                Reply(HttpMethod.Get, "/v0/users/test-user/collections/8", Status(HttpStatusCode.NotFound)),
                Reply(HttpMethod.Post, "/v0/users/-/collections/8", Status(HttpStatusCode.Accepted)),
                Reply(HttpMethod.Get, "/v0/users/-/collections/-/episodes/10", EpisodeCollection(0)),
                Reply(HttpMethod.Patch, "/v0/users/-/collections/8/episodes", Status(HttpStatusCode.NoContent)));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            await client.MarkEpisodeAsync(8, 10, Token, () => true, CancellationToken.None);
            var mutations = transport.Sent.Where(request => request.Method != HttpMethod.Get).ToArray();
            Assert.Equal(2, mutations.Length);
            Assert.Equal("application/json", mutations[0].ContentType);
            Assert.Equal("application/json", mutations[1].ContentType);
            using var created = JsonDocument.Parse(mutations[0].Body);
            Assert.Equal(3, created.RootElement.GetProperty("type").GetInt32());
            Assert.Equal(1, created.RootElement.EnumerateObject().Count());
            using var marked = JsonDocument.Parse(mutations[1].Body);
            Assert.Equal(2, marked.RootElement.GetProperty("type").GetInt32());
            var ids = marked.RootElement.GetProperty("episode_id");
            Assert.Equal(1, ids.GetArrayLength());
            Assert.Equal(10L, ids[0].GetInt64());
            Assert.Equal(2, marked.RootElement.EnumerateObject().Count());
            transport.AssertComplete();
        });

        foreach (var subjectType in new[] { 1, 2, 3, 4, 5 })
        {
            var capturedType = subjectType;
            Case($"Bangumi：已有收藏状态 {capturedType} 时保留原条目，仅更新单话", async () =>
            {
                using var transport = Existing(capturedType, 0, true);
                using var http = new HttpClient(transport);
                using var client = new BangumiClient(http);
                await client.MarkEpisodeAsync(8, 10, Token, () => true, CancellationToken.None);
                Assert.False(transport.Sent.Any(request => request.Method == HttpMethod.Post));
                Assert.Equal(1, transport.Sent.Count(request => request.Method == HttpMethod.Patch));
                transport.AssertComplete();
            });
        }

        Case("Bangumi：单话已看过时保留时间记录，不重复 PATCH", async () =>
        {
            using var transport = Existing(3, 2);
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            await client.MarkEpisodeAsync(8, 10, Token, () => true, CancellationToken.None);
            Assert.True(transport.Sent.All(request => request.Method == HttpMethod.Get));
            transport.AssertComplete();
        });

        Case("Bangumi：每个请求单独附加 Bearer，令牌不进入 URL 或默认请求头", async () =>
        {
            using var transport = Existing(3, 0, true);
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            await client.MarkEpisodeAsync(8, 10, Token, () => true, CancellationToken.None);
            Assert.Null(http.DefaultRequestHeaders.Authorization);
            foreach (var request in transport.Sent)
            {
                Assert.Equal("Bearer", request.AuthScheme);
                Assert.Equal(Token, request.AuthValue);
                Assert.Equal("https", request.Uri.Scheme);
                Assert.Equal("api.bgm.tv", request.Uri.Host);
                Assert.DoesNotContain(Token, request.Uri.AbsoluteUri);
                Assert.False(request.Uri.Query.Contains("token", StringComparison.OrdinalIgnoreCase));
                Assert.False(request.HasCookie);
                Assert.Contains("Momoka", request.UserAgent);
            }
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：空令牌和控制字符在发请求前拒绝", () =>
        {
            using var transport = new Transport();
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            foreach (var token in new[] { "", "   ", "bad\r\nheader" })
                Assert.Throws<InvalidOperationException>(() => client.SubjectAsync(8, token, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Sent.Count);
        });

        Case("Bangumi：条目优先中文名称并读取本篇总集数", async () =>
        {
            using var transport = new Transport(Reply(HttpMethod.Get, "/v0/subjects/8",
                Json(new { id = 8, type = 2, name_cn = "测试作品", name = "Fixture", eps = 12 })));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            var subject = await client.SubjectAsync(8, Token, CancellationToken.None);
            Assert.Equal(8L, subject.Id);
            Assert.Equal("测试作品", subject.Title);
            Assert.Equal<int?>(12, subject.EpisodeCount);
            transport.AssertComplete();
        });

        Case("Bangumi：未知集数保留为空且无中文名时使用原名", async () =>
        {
            using var transport = new Transport(Reply(HttpMethod.Get, "/v0/subjects/8",
                Json(new { id = 8, type = 2, name_cn = "", name = "Fixture", eps = 0 })));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            var subject = await client.SubjectAsync(8, Token, CancellationToken.None);
            Assert.Equal("Fixture", subject.Title);
            Assert.Null(subject.EpisodeCount);
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：所读编号一致也不能将非动画条目用于同步", () =>
        {
            using var transport = new Transport(Reply(HttpMethod.Get, "/v0/subjects/8",
                Json(new { id = 8, type = 4, name_cn = "测试游戏", eps = 12 })));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            Assert.Throws<InvalidOperationException>(() => client.SubjectAsync(8, Token, CancellationToken.None)
                .GetAwaiter().GetResult());
            transport.AssertComplete();
        });

        Case("Bangumi：搜索请求设置动画筛选并保留年份和集数，使用 Bearer 与固定 API 来源", async () =>
        {
            using var transport = new Transport(request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v0/search/subjects", request.Uri.AbsolutePath);
                Assert.Equal("?limit=20", request.Uri.Query);
                Assert.Equal("https", request.Uri.Scheme);
                Assert.Equal("api.bgm.tv", request.Uri.Host);
                Assert.Equal("Bearer", request.AuthScheme);
                Assert.Equal(Token, request.AuthValue);
                using var body = JsonDocument.Parse(request.Body);
                Assert.Equal("测试作品", body.RootElement.GetProperty("keyword").GetString());
                Assert.Equal(2, body.RootElement.GetProperty("filter").GetProperty("type")[0].GetInt32());
                return Json(new
                {
                    data = new[]
                    {
                        new { id = 8, name_cn = "测试作品", name = "Fixture", date = "2024-01-01", eps = 12 },
                        new { id = 9, name_cn = "测试游戏", name = "Game", date = "2024", eps = 0 }
                    }
                });
            });
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            var results = await client.SearchSubjectsAsync("测试作品", Token, CancellationToken.None);
            Assert.Equal(2, results.Count);
            Assert.Equal("测试作品", results[0].Title);
            Assert.Equal<int?>(2024, results[0].Year);
            Assert.Equal<int?>(12, results[0].EpisodeCount);
            Assert.Equal("Fixture", results[0].Detail);
            Assert.Equal("测试游戏", results[1].Title);
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：空标题或空令牌不发搜索请求", () =>
        {
            using var transport = new Transport();
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            Assert.Throws<ArgumentException>(() => client.SearchSubjectsAsync(" ", Token, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => client.SearchSubjectsAsync("测试", " ", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Sent.Count);
        });

        Case("Bangumi：章节超过一页按 offset 继续获取", async () =>
        {
            using var transport = new Transport(
                Reply(HttpMethod.Get, "/v0/episodes", EpisodePage(1, 200, 201)),
                Reply(HttpMethod.Get, "/v0/episodes", EpisodePage(201, 1, 201)));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            var episodes = await client.GetEpisodesAsync(8, Token, CancellationToken.None);
            Assert.Equal(201, episodes.Count);
            Assert.Equal(1, episodes[0].Number);
            Assert.Equal(201, episodes[^1].Number);
            Assert.Equal(2, transport.Sent.Count);
            Assert.Contains("subject_id=8", transport.Sent[0].Uri.Query);
            Assert.Contains("type=0", transport.Sent[0].Uri.Query);
            Assert.Contains("limit=200", transport.Sent[0].Uri.Query);
            Assert.Contains("offset=0", transport.Sent[0].Uri.Query);
            Assert.Contains("offset=200", transport.Sent[1].Uri.Query);
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：重复集号和重复章节 ID 拒绝映射", () =>
        {
            foreach (var duplicate in new[]
            {
                new[] { new { id = 1, type = 0, sort = 1m }, new { id = 2, type = 0, sort = 1m } },
                new[] { new { id = 1, type = 0, sort = 1m }, new { id = 1, type = 0, sort = 2m } }
            })
            {
                using var transport = new Transport(Reply(HttpMethod.Get, "/v0/episodes", Json(new { total = 2, data = duplicate })));
                using var http = new HttpClient(transport);
                using var client = new BangumiClient(http);
                Assert.Throws<InvalidOperationException>(() => client.GetEpisodesAsync(8, Token, CancellationToken.None).GetAwaiter().GetResult());
                transport.AssertComplete();
            }
        });

        Case("Bangumi：分数集号、零集号与非本篇不映射为正常集", async () =>
        {
            using var transport = new Transport(Reply(HttpMethod.Get, "/v0/episodes", Json(new
            {
                total = 4,
                data = new[]
                {
                    new { id = 1, type = 0, sort = 1m },
                    new { id = 2, type = 0, sort = 1.5m },
                    new { id = 3, type = 0, sort = 0m },
                    new { id = 4, type = 1, sort = 2m }
                }
            })));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            var episodes = await client.GetEpisodesAsync(8, Token, CancellationToken.None);
            Assert.Equal(1, episodes.Count);
            Assert.Equal(1L, episodes[0].Id);
            Assert.Equal(1, episodes[0].Number);
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：HTTP 失败只报告状态，不泄漏服务端正文", () =>
        {
            const string responseSecret = "response-contains-synthetic-private-data";
            using var transport = new Transport(Reply(HttpMethod.Get, "/v0/subjects/8",
                new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(responseSecret) }));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            var error = Assert.Catch<HttpRequestException>(() => client.SubjectAsync(8, Token, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("401", error.Message);
            Assert.DoesNotContain(responseSecret, error.ToString());
            Assert.DoesNotContain(Token, error.ToString());
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：响应超过上限时停止解析", () =>
        {
            using var transport = new Transport(Reply(HttpMethod.Get, "/v0/subjects/8",
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 2 * 1024 * 1024 + 1)) }));
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            Assert.Throws<InvalidOperationException>(() => client.SubjectAsync(8, Token, CancellationToken.None).GetAwaiter().GetResult());
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：设置在收藏读取后关闭时不创建收藏", () =>
        {
            var allowed = true;
            using var transport = new Transport(
                Reply(HttpMethod.Get, "/v0/me", Me()),
                request =>
                {
                    Assert.Equal("/v0/users/test-user/collections/8", request.Uri.AbsolutePath);
                    allowed = false;
                    return Status(HttpStatusCode.NotFound);
                });
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            Assert.Throws<OperationCanceledException>(() => client.MarkEpisodeAsync(8, 10, Token, () => allowed, CancellationToken.None).GetAwaiter().GetResult());
            Assert.True(transport.Sent.All(request => request.Method == HttpMethod.Get));
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：设置在章节读取后关闭时不写入单话", () =>
        {
            var allowed = true;
            using var transport = new Transport(
                Reply(HttpMethod.Get, "/v0/me", Me()),
                Reply(HttpMethod.Get, "/v0/users/test-user/collections/8", Json(new { type = 3 })),
                request =>
                {
                    Assert.Equal("/v0/users/-/collections/-/episodes/10", request.Uri.AbsolutePath);
                    allowed = false;
                    return EpisodeCollection(0);
                });
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            Assert.Throws<OperationCanceledException>(() => client.MarkEpisodeAsync(8, 10, Token, () => allowed, CancellationToken.None).GetAwaiter().GetResult());
            Assert.True(transport.Sent.All(request => request.Method == HttpMethod.Get));
            transport.AssertComplete();
        });

        TestHarness.Test("Bangumi：已取消任务与关闭的开关不发请求", () =>
        {
            using var transport = new Transport();
            using var http = new HttpClient(transport);
            using var client = new BangumiClient(http);
            Assert.Throws<OperationCanceledException>(() => client.MarkEpisodeAsync(8, 10, Token, () => false, CancellationToken.None).GetAwaiter().GetResult());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => client.MarkEpisodeAsync(8, 10, Token, () => true, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Sent.Count);
        });
    }

    private static Transport Existing(int subjectType, int episodeType, bool write = false)
    {
        var replies = new List<Func<Sent, HttpResponseMessage>>
        {
            Reply(HttpMethod.Get, "/v0/me", Me()),
            Reply(HttpMethod.Get, "/v0/users/test-user/collections/8", Json(new { type = subjectType })),
            Reply(HttpMethod.Get, "/v0/users/-/collections/-/episodes/10", EpisodeCollection(episodeType))
        };
        if (write) replies.Add(Reply(HttpMethod.Patch, "/v0/users/-/collections/8/episodes", Status(HttpStatusCode.NoContent)));
        return new Transport(replies.ToArray());
    }

    private static HttpResponseMessage Me() => Json(new { id = 1, username = "test-user" });
    private static HttpResponseMessage EpisodeCollection(int type) => Json(new { type, episode = new { id = 10, subject_id = 8 } });
    private static HttpResponseMessage EpisodePage(int first, int count, int total) => Json(new
    {
        total,
        data = Enumerable.Range(first, count).Select(number => new { id = number, type = 0, sort = number }).ToArray()
    });
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };
    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);
    private static Func<Sent, HttpResponseMessage> Reply(HttpMethod method, string path, HttpResponseMessage response) => request =>
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(path, request.Uri.AbsolutePath);
        return response;
    };
    private static void Case(string name, Func<Task> action) => TestHarness.Test(name, () => action().GetAwaiter().GetResult());

    private sealed record Sent(HttpMethod Method, Uri Uri, string Body, string? ContentType, string? AuthScheme,
        string? AuthValue, string UserAgent, bool HasCookie);

    private sealed class Transport(params Func<Sent, HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        private readonly Queue<Func<Sent, HttpResponseMessage>> _replies = new(replies);
        public List<Sent> Sent { get; } = [];
        public void AssertComplete() => Assert.Equal(0, _replies.Count);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sent = new Sent(request.Method, request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Content?.Headers.ContentType?.ToString(), request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter, request.Headers.UserAgent.ToString(), request.Headers.Contains("Cookie"));
            Sent.Add(sent);
            Assert.True(_replies.Count > 0, "出现预期外的 Bangumi 请求");
            return _replies.Dequeue()(sent);
        }
    }
}
