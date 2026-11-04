using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Proves the existing range adapters used by deep return, without a session or authentication.</summary>
internal static class DeepBrowseProtocolTests
{
    public static void Register()
    {
        foreach (var (start, size, requests) in new[] { (1480, 40, 1), (1497, 40, 2), (1993, 40, 1), (2000, 40, 1) })
        {
            Test($"深返回协议：飞牛从 {start} 有界读取 {size} 项", () =>
            {
                using var transport = new RangeTransport(fnnas: true);
                using var http = new FnNasHttp(transport);
                var adapter = new FnNasMediaAdapter(new FnNasClient(http),
                    new FnNasConnection(new Uri("https://range.invalid/v/"), "fixture-only"));
                var result = adapter.GetItemsAsync(Query(start, size), default).GetAwaiter().GetResult();
                CheckResult(result, start, size);
                Assert.Equal(requests, transport.Ranges.Count, "只能读取所在页与必要的相邻一页");
                Assert.Equal(start / size * size, transport.Ranges[0].Start);
                Assert.True(transport.Ranges.All(range => range.Size == size), "不能扩大单次请求取回历史前缀");
                if (requests == 2) Assert.Equal(transport.Ranges[0].Start + size, transport.Ranges[1].Start);
            });

            Test($"深返回协议：Plex 从 {start} 单次读取 {size} 项", () =>
            {
                using var transport = new RangeTransport(fnnas: false);
                using var http = new PlexHttp(transport);
                var adapter = new PlexMediaAdapter(new PlexClient(http),
                    new PlexConnection(new Uri("https://range.invalid/"), "fixture-only"));
                var result = adapter.GetItemsAsync(Query(start, size), default).GetAwaiter().GetResult();
                CheckResult(result, start, size);
                Assert.Equal(1, transport.Ranges.Count, "Plex 范围读取不能先重放前面的页");
                Assert.Equal((start, size), transport.Ranges.Single());
            });
        }
    }

    private static ItemQuery Query(int start, int size) => new()
    {
        ParentId = "library", StartIndex = start, Limit = size,
        SortBy = EmbySortBy.Name, Descending = true, IncludeItemTypes = [EmbyItemType.Movie]
    };

    private static void CheckResult(ItemsResult result, int start, int size)
    {
        Assert.Equal(2000, result.TotalRecordCount, "总数仍是整个库而不是窗口长度");
        Assert.Equal(string.Join(',', Enumerable.Range(start, Math.Max(0, Math.Min(size, 2000 - start)))),
            string.Join(',', result.Items.Select(item => item.Id)), "返回绝对区间不能有缺口或重复");
    }

    private sealed class RangeTransport(bool fnnas) : HttpMessageHandler
    {
        public List<(int Start, int Size)> Ranges { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("range.invalid", request.RequestUri!.Host);
            int start;
            int size;
            string json;
            if (fnnas)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v/api/v1/item/list", request.RequestUri.AbsolutePath);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                size = body["page_size"]!.GetValue<int>();
                start = (body["page"]!.GetValue<int>() - 1) * size;
                Assert.Equal("library", body["ancestor_guid"]!.GetValue<string>());
                Assert.Equal("sort_title", body["sort_column"]!.GetValue<string>());
                Assert.Equal("DESC", body["sort_type"]!.GetValue<string>());
                var items = Indices(start, size).Select(index => new { guid = index.ToString(CultureInfo.InvariantCulture), title = "匿名电影", type = "Movie" });
                json = JsonSerializer.Serialize(new { code = 0, data = new { total = 2000, list = items } });
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/library/sections/library/all", request.RequestUri.AbsolutePath);
                start = int.Parse(request.Headers.GetValues("X-Plex-Container-Start").Single(), CultureInfo.InvariantCulture);
                size = int.Parse(request.Headers.GetValues("X-Plex-Container-Size").Single(), CultureInfo.InvariantCulture);
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                Assert.Contains("sort=titleSort:desc", query);
                Assert.False(query.Contains("Container", StringComparison.Ordinal), "分页值必须在请求头里");
                var items = Indices(start, size).Select(index => new { ratingKey = index.ToString(CultureInfo.InvariantCulture), title = "匿名电影", type = "movie" });
                json = JsonSerializer.Serialize(new { MediaContainer = new { totalSize = 2000, Metadata = items } });
            }
            Ranges.Add((start, size));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static IEnumerable<int> Indices(int start, int size) =>
            Enumerable.Range(start, Math.Max(0, Math.Min(size, 2000 - start)));
    }
}
