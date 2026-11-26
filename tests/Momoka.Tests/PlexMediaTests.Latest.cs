using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterLatestPaging()
    {
        Test("Plex 最新：混合原始页按类型跨页补足并保留顺序", () =>
        {
            using var f = new LatestFixture(45, [19, 20, 42, 44]);
            Assert.Equal("latest-19,latest-20,latest-42", string.Join(',', f.Read(3).Select(item => item.Id)));
            Assert.True(f.Transport.Starts.SequenceEqual([0, 20, 40]));
        });
        Test("Plex 最新：同页足量立即结束，不请求后页", () =>
        {
            using var f = new LatestFixture(45, [0, 1, 30]);
            Assert.Equal("latest-0,latest-1", string.Join(',', f.Read(2).Select(item => item.Id)));
            Assert.Equal(1, f.Transport.Starts.Count);
        });
        Test("Plex 最新：类型表去空白和空项，支持电影与剧集联合", () =>
        {
            using var f = new LatestFixture(4, [1, 3]);
            f.Transport.KindOverrides[3] = "show";
            var rows = f.Read(5, " Movie, ,Series, ");
            Assert.Equal("latest-1,latest-3", string.Join(',', rows.Select(item => item.Id)));
            Assert.Equal("Movie,Series", string.Join(',', rows.Select(item => item.Type)));
            Assert.Equal(1, f.Transport.Starts.Count);
        });
        foreach (var types in new string?[] { null, "", "  ", " , , " })
            Test("Plex 最新：空类型保留原始单请求契约 " + (types ?? "null"), () =>
            {
                using var f = new LatestFixture(45, [20]);
                Assert.Equal(3, f.Read(3, types).Count);
                Assert.True(f.Transport.Starts.SequenceEqual([0]));
                Assert.True(f.Transport.Sizes.SequenceEqual([3]));
            });
        Test("Plex 最新：字符串总数及服务端短页按原始数量推进", () =>
        {
            using var f = new LatestFixture(21, [20]);
            f.Transport.StringTotal = true; f.Transport.PageLimit = 5;
            Assert.Equal("latest-20", f.Read(1).Single().Id);
            Assert.True(f.Transport.Starts.SequenceEqual([0, 5, 10, 15, 20]));
        });
        Test("Plex 最新：缺总数的满页继续寻找匹配，短页结束", () =>
        {
            using var f = new LatestFixture(21, [20]);
            f.Transport.IncludeTotal = false;
            Assert.Equal("latest-20", f.Read(1).Single().Id);
            Assert.True(f.Transport.Starts.SequenceEqual([0, 20]));
        });
        Test("Plex 最新：缺总数且没有匹配，完整原始页之后读取空尾页", () =>
        {
            using var f = new LatestFixture(40, []);
            f.Transport.IncludeTotal = false;
            Assert.Equal(0, f.Read(1).Count);
            Assert.True(f.Transport.Starts.SequenceEqual([0, 20, 40]));
        });
        Test("Plex 最新：空页即使声明更多数据也终止", () =>
        {
            using var f = new LatestFixture(0, []); f.Transport.Total = 100;
            Assert.Equal(0, f.Read(1).Count);
            Assert.Equal(1, f.Transport.Starts.Count);
        });
        Test("Plex 最新：后页失败传播，不把已读部分冒充完整结果", () =>
        {
            using var f = new LatestFixture(45, [0, 42]); f.Transport.FailAt = 20;
            Assert.Throws<PlexApiException>(() => f.Read(2));
            Assert.True(f.Transport.Starts.SequenceEqual([0, 20]));
        });
        Test("Plex 最新：后页取消传播，不返回部分匹配", () =>
        {
            using var f = new LatestFixture(45, [0, 42]);
            using var cancel = new CancellationTokenSource();
            f.Transport.OnPage = start => { if (start == 20) cancel.Cancel(); };
            Assert.Throws<OperationCanceledException>(() => f.Read(2, ct: cancel.Token));
            Assert.True(f.Transport.Starts.SequenceEqual([0, 20]));
        });
        Test("Plex 最新：已取消时零请求", () =>
        {
            using var f = new LatestFixture(21, [20]);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => f.Read(1, ct: cancel.Token));
            Assert.Equal(0, f.Transport.Starts.Count);
        });
        foreach (var limit in new[] { 0, -1 })
            Test("Plex 最新：非正限量零请求 " + limit, () =>
            {
                using var f = new LatestFixture(21, [20]);
                Assert.Equal(0, f.Read(limit).Count); Assert.Equal(0, f.Transport.Starts.Count);
            });
        Test("Plex 最新：per-library保持section路由，大小写不扩展类型契约", () =>
        {
            using var f = new LatestFixture(4, [1]);
            Assert.Equal(0, f.Read(1, "movie", "library").Count);
            Assert.True(f.Transport.Paths.All(path => path == "/library/sections/library/recentlyAdded"));
        });
    }

    private sealed class LatestFixture : IDisposable
    {
        private readonly PlexHttp _http;
        private readonly PlexMediaAdapter _adapter;
        public LatestTransport Transport { get; }
        public LatestFixture(int count, int[] targets)
        {
            Transport = new(count, targets); _http = new PlexHttp(Transport);
            _adapter = new(new PlexClient(_http), new PlexConnection(new Uri("http://plex-latest.invalid/"), "placeholder-only"));
        }
        public List<EmbyItem> Read(int limit, string? types = EmbyItemType.Movie, string? parent = null, CancellationToken ct = default) =>
            _adapter.GetLatestAsync(parent, limit, types, ct).GetAwaiter().GetResult();
        public void Dispose() => _http.Dispose();
    }
    private sealed class LatestTransport(int count, int[] targets) : HttpMessageHandler
    {
        public List<int> Starts { get; } = [];
        public List<int> Sizes { get; } = [];
        public List<string> Paths { get; } = [];
        public Dictionary<int, string> KindOverrides { get; } = [];
        public bool IncludeTotal { get; set; } = true;
        public bool StringTotal { get; set; }
        public int? Total { get; set; }
        public int? PageLimit { get; set; }
        public int? FailAt { get; set; }
        public Action<int>? OnPage { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("plex-latest.invalid", request.RequestUri!.Host);
            Assert.Equal("", request.RequestUri.Query, "do not invent a server-side type parameter");
            var start = int.Parse(request.Headers.GetValues("X-Plex-Container-Start").Single());
            var size = int.Parse(request.Headers.GetValues("X-Plex-Container-Size").Single());
            Starts.Add(start); Sizes.Add(size); Paths.Add(request.RequestUri.AbsolutePath);
            OnPage?.Invoke(start); ct.ThrowIfCancellationRequested();
            if (FailAt == start) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var rows = new JsonArray();
            foreach (var index in Enumerable.Range(0, count).Skip(start).Take(Math.Min(size, PageLimit ?? size)))
                rows.Add(new JsonObject { ["ratingKey"] = "latest-" + index, ["title"] = "anonymous", ["type"] = KindOverrides.GetValueOrDefault(index, targets.Contains(index) ? "movie" : "track") });
            var page = new JsonObject { ["size"] = rows.Count, ["Metadata"] = rows };
            if (IncludeTotal) page["totalSize"] = StringTotal ? JsonValue.Create((Total ?? count).ToString()) : JsonValue.Create(Total ?? count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(new JsonObject { ["MediaContainer"] = page }.ToJsonString()) });
        }
    }
}
