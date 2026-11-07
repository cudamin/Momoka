using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterNextUpPaging()
    {
        Test("Plex 下一集：目标在第21条时继续读取原始页并保留续播替换", () =>
        {
            using var fixture = new NextUpFixture(21, [20]);
            var next = fixture.Read(1).Single();
            Assert.Equal("episode-20", next.Id);
            Assert.Equal("0,20", string.Join(',', fixture.Transport.Starts));
            Assert.Equal("20,20", string.Join(',', fixture.Transport.Sizes));
            var watched = new EmbyItem { Id = "watched", Type = EmbyItemType.Episode, SeriesId = "target" };
            Assert.Equal("episode-20", ResumeRow.SlotAfterWatched(watched, next)!.Id);
        });
        Test("Plex 下一集：跨页按原始条目推进并保持目标剧顺序和限量", () =>
        {
            using var fixture = new NextUpFixture(45, [19, 20, 42, 44]);
            Assert.Equal("episode-19,episode-20,episode-42", string.Join(',', fixture.Read(3).Select(item => item.Id)));
            Assert.Equal("0,20,40", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：前页已经足量时不请求后页", () =>
        {
            using var fixture = new NextUpFixture(45, [0, 20]);
            Assert.Equal("episode-0", fixture.Read(1).Single().Id);
            Assert.Equal(1, fixture.Transport.Starts.Count);
        });
        Test("Plex 下一集：字符串总数与服务端短页按实际原始数量推进", () =>
        {
            using var fixture = new NextUpFixture(21, [20]);
            fixture.Transport.StringTotal = true;
            fixture.Transport.PageLimit = 5;
            Assert.Equal("episode-20", fixture.Read(1).Single().Id);
            Assert.Equal("0,5,10,15,20", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：缺总数的满页仍继续寻找后页目标", () =>
        {
            using var fixture = new NextUpFixture(21, [20]);
            fixture.Transport.IncludeTotal = false;
            Assert.Equal("episode-20", fixture.Read(1).Single().Id);
            Assert.Equal("0,20", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：缺总数且整页无目标时读取空尾页后结束", () =>
        {
            using var fixture = new NextUpFixture(40, []);
            fixture.Transport.IncludeTotal = false;
            Assert.Equal(0, fixture.Read(1).Count);
            Assert.Equal("0,20,40", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：缺总数的短页确认结束", () =>
        {
            using var fixture = new NextUpFixture(3, []);
            fixture.Transport.IncludeTotal = false;
            Assert.Equal(0, fixture.Read(1).Count);
            Assert.Equal(1, fixture.Transport.Starts.Count);
        });
        Test("Plex 下一集：读完整个总数仍无目标才返回空", () =>
        {
            using var fixture = new NextUpFixture(25, []);
            Assert.Equal(0, fixture.Read(1).Count);
            Assert.Equal("0,20", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：空页即使声称有总数也结束", () =>
        {
            using var fixture = new NextUpFixture(0, []);
            fixture.Transport.Total = 50;
            Assert.Equal(0, fixture.Read(1).Count);
            Assert.Equal(1, fixture.Transport.Starts.Count);
        });
        Test("Plex 下一集：后页失败不冒充无下一集或返回部分目标", () =>
        {
            using var fixture = new NextUpFixture(25, [0, 24]);
            fixture.Transport.FailAt = 20;
            Assert.Throws<PlexApiException>(() => fixture.Read(2));
            Assert.Equal("0,20", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：后页取消传播而不返回空或部分目标", () =>
        {
            using var fixture = new NextUpFixture(25, [0, 24]);
            using var canceled = new CancellationTokenSource();
            fixture.Transport.OnPage = start => { if (start == 20) canceled.Cancel(); };
            Assert.Throws<OperationCanceledException>(() => fixture.Read(2, ct: canceled.Token));
            Assert.Equal("0,20", string.Join(',', fixture.Transport.Starts));
        });
        Test("Plex 下一集：已取消请求不发送HTTP", () =>
        {
            using var fixture = new NextUpFixture(21, [20]);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Read(1, ct: canceled.Token));
            Assert.Equal(0, fixture.Transport.Starts.Count);
        });
        foreach (var (series, limit) in new (string?, int)[] { (null, 1), ("", 1), ("  ", 1), ("target", 0), ("target", -1) })
            Test($"Plex 下一集：无效输入 {series ?? "null"}/{limit} 不读取服务器", () =>
            {
                using var fixture = new NextUpFixture(21, [20]);
                Assert.Equal(0, fixture.Read(limit, series).Count);
                Assert.Equal(0, fixture.Transport.Starts.Count);
            });
    }

    private sealed class NextUpFixture : IDisposable
    {
        private readonly PlexHttp _http;
        private readonly PlexMediaAdapter _adapter;
        public NextUpTransport Transport { get; }

        public NextUpFixture(int count, int[] targets)
        {
            Transport = new NextUpTransport(count, targets);
            _http = new PlexHttp(Transport);
            _adapter = new PlexMediaAdapter(new PlexClient(_http),
                new PlexConnection(new Uri("http://plex.invalid/"), "fixture-token"));
        }

        public List<EmbyItem> Read(int limit, string? series = "target", CancellationToken ct = default) =>
            _adapter.GetNextUpAsync(series, limit, ct).GetAwaiter().GetResult();

        public void Dispose() => _http.Dispose();
    }

    private sealed class NextUpTransport(int count, int[] targets) : HttpMessageHandler
    {
        public List<int> Starts { get; } = [];
        public List<int> Sizes { get; } = [];
        public bool IncludeTotal { get; set; } = true;
        public bool StringTotal { get; set; }
        public int? Total { get; set; }
        public int? PageLimit { get; set; }
        public int? FailAt { get; set; }
        public Action<int>? OnPage { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/library/onDeck", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            var start = int.Parse(request.Headers.GetValues("X-Plex-Container-Start").Single());
            var size = int.Parse(request.Headers.GetValues("X-Plex-Container-Size").Single());
            Starts.Add(start);
            Sizes.Add(size);
            OnPage?.Invoke(start);
            ct.ThrowIfCancellationRequested();
            if (FailAt == start) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("fixture failure") });

            var rows = new JsonArray();
            foreach (var i in Enumerable.Range(0, count).Skip(start).Take(Math.Min(size, PageLimit ?? size)))
                rows.Add(new JsonObject
                {
                    ["ratingKey"] = "episode-" + i,
                    ["type"] = "episode",
                    ["title"] = "匿名集" + i,
                    ["grandparentRatingKey"] = targets.Contains(i) ? "target" : "other",
                    ["parentRatingKey"] = "season",
                    ["index"] = i + 1
                });
            var container = new JsonObject { ["size"] = rows.Count, ["Metadata"] = rows };
            if (IncludeTotal) container["totalSize"] = StringTotal ? JsonValue.Create((Total ?? count).ToString()) : JsonValue.Create(Total ?? count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(new JsonObject { ["MediaContainer"] = container }.ToJsonString()) });
        }
    }
}
