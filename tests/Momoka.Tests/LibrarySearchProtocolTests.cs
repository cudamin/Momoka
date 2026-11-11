using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LibrarySearchProtocolTests
{
    internal static void Register()
    {
        Test("搜索扩展：两家人物合集列表按真实协议读取，既有分类数值保留", () =>
        {
            Assert.Equal(0, (int)LibrarySearchCategory.TopResults);
            Assert.Equal(3, (int)LibrarySearchCategory.Episodes);
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
                foreach (var (category, type) in new[] { ("People", "Person"), ("Collections", "BoxSet"), ("Playlists", "Playlist") })
                {
                    using var fixture = new SearchFixture(kind);
                    var chosen = Category(category);
                    var result = Search(fixture.Client, "  名称 & 测试  ", chosen, 1, 1);
                    Assert.Equal(type + "-1", result.Items.Single().Id);
                    var sent = fixture.Transport.Sent.Single();
                    Assert.Equal("名称 & 测试", sent.Query["SearchTerm"]);
                    Assert.True(sent.Uri.Query.Contains("%26", StringComparison.Ordinal));
                    if (kind == MediaServerKind.Jellyfin && type == "Person")
                    {
                        Assert.Equal("/Persons", sent.Uri.AbsolutePath);
                        Assert.Equal("user", sent.Query["UserId"]);
                        Assert.Equal("0", sent.Query["Limit"]);
                        Assert.False(sent.Query.ContainsKey("StartIndex"), "旧服务器不支持人物分页，不能重复第一页");
                        Assert.Equal(3, result.TotalRecordCount);
                    }
                    else
                    {
                        Assert.Equal(kind == MediaServerKind.Emby ? "/emby/Users/user/Items" : "/Users/user/Items", sent.Uri.AbsolutePath);
                        Assert.Equal(type, sent.Query["IncludeItemTypes"]);
                        Assert.Equal("1", sent.Query["StartIndex"]);
                        Assert.Equal("1", sent.Query["Limit"]);
                        Assert.False(sent.Query.ContainsKey("SortBy"));
                    }
                    Assert.False(result.Items.Single().IsPlayable);
                }
        });

        Test("搜索扩展：热门媒体与人物分页相互独立并有正确总数", () =>
        {
            using var fixture = new SearchFixture(MediaServerKind.Jellyfin);
            var mediaFirst = Search(fixture.Client, "词", LibrarySearchCategory.TopResults, 0, 2);
            var peopleFirst = People(fixture.Client, "词", 0, 2);
            var mediaLast = Search(fixture.Client, "词", LibrarySearchCategory.TopResults, 2, 2);
            var peopleLast = People(fixture.Client, "词", 2, 2);
            Assert.Equal("Movie-0,Movie-1", string.Join(',', mediaFirst.Items.Select(item => item.Id)));
            Assert.Equal("Person-0,Person-1", string.Join(',', peopleFirst.Items.Select(item => item.Id)));
            Assert.Equal("Movie-2", mediaLast.Items.Single().Id);
            Assert.Equal("Person-2", peopleLast.Items.Single().Id);
            Assert.Equal(3, peopleFirst.TotalRecordCount);
            Assert.Equal(0, People(fixture.Client, "词", 3, 2).Items.Count);
            Assert.Equal(3, People(fixture.Client, "词", 3, 2).TotalRecordCount);
            Assert.True(fixture.Transport.Sent.Where(sent => sent.Uri.AbsolutePath == "/Users/user/Items")
                .All(sent => !sent.Query["IncludeItemTypes"].Contains("Person", StringComparison.Ordinal)));
        });

        Test("搜索扩展：空词和取消不发请求，非法分页和无实现类别明确拒绝", () =>
        {
            using var fixture = new SearchFixture(MediaServerKind.Jellyfin);
            Assert.Equal(0, Search(fixture.Client, "  ", LibrarySearchCategory.TopResults, 0, 20).Items.Count);
            Assert.Equal(0, People(fixture.Client, "", 0, 20).Items.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => Search(fixture.Client, "词", LibrarySearchCategory.TopResults, -1, 20));
            Assert.Throws<ArgumentOutOfRangeException>(() => People(fixture.Client, "词", 0, 0));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => People(fixture.Client, "词", 0, 20, cancelled.Token));
            foreach (var kind in new[] { MediaServerKind.Plex, MediaServerKind.FnNas })
            {
                var client = new EmbyClient(fixture.Http, Connection(kind));
                Assert.Throws<NotSupportedException>(() => Search(client, "词", Category("People"), 0, 20));
                Assert.Throws<NotSupportedException>(() => Search(client, "词", Category("Collections"), 0, 20));
                Assert.Throws<NotSupportedException>(() => Search(client, "词", Category("Playlists"), 0, 20));
            }
            Assert.Equal(0, fixture.Transport.Sent.Count);
        });

        Test("搜索扩展：两家热门范围增加可打开实体，Plex飞牛仍用既有媒体范围", () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                using var fixture = new SearchFixture(kind);
                Search(fixture.Client, "词", LibrarySearchCategory.TopResults, 0, 20);
                var types = fixture.Transport.Sent.Single().Query["IncludeItemTypes"].Split(',');
                Assert.True(types.Contains("BoxSet") && types.Contains("Playlist"));
                Assert.Equal(kind == MediaServerKind.Emby, types.Contains("Person"));
            }
        });

        Test("搜索扩展：人物全集读取失败或取消必须传播，不回不完整页", () =>
        {
            using var fixture = new SearchFixture(MediaServerKind.Jellyfin);
            fixture.Transport.Failure = HttpStatusCode.Forbidden;
            Assert.Throws<EmbyApiException>(() => People(fixture.Client, "词", 0, 1));
            fixture.Transport.Failure = null;
            using var cancellation = new CancellationTokenSource();
            fixture.Transport.BeforeReply = cancellation.Cancel;
            Assert.Throws<OperationCanceledException>(() => People(fixture.Client, "词", 0, 1, cancellation.Token));
        });

        Test("搜索实体：人物合集列表有独立分组，最近记录保留各自类型与标识", () =>
        {
            foreach (var (type, title) in new[] { ("Person", "人物"), ("BoxSet", "合集"), ("Playlist", "播放列表") })
            {
                Assert.Equal(title, LibrarySearchGroups.TitleOf(LibrarySearchGroups.KindOf(type)));
                var ui = new Momoka.Configuration.UiSettings();
                RecentSearch.Remember(ui, Connection(MediaServerKind.Emby), "词", new EmbyItem { Id = type + "-1", Type = type, Name = title });
                var item = RecentSearch.Read(ui, Connection(MediaServerKind.Emby)).Single().ToItem();
                Assert.Equal(type, item.Type);
                Assert.Equal(type + "-1", item.Id);
                Assert.False(item.IsPlayable);
            }
        });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"播放列表读取：{kind}使用成员协议原序翻页，空列表与读取拒绝保留语义", () =>
            {
                using var fixture = new SearchFixture(kind);
                var first = Playlist(fixture.Client, "list", 0, 2);
                var last = Playlist(fixture.Client, "list", 2, 2);
                Assert.Equal("Z,A", string.Join(',', first.Items.Select(item => item.Id)));
                Assert.Equal("M", last.Items.Single().Id);
                Assert.Equal(3, first.TotalRecordCount);
                var sent = fixture.Transport.Sent.First();
                Assert.Equal(kind == MediaServerKind.Jellyfin ? "/Playlists/list/Items" : "/emby/Users/user/Items", sent.Uri.AbsolutePath);
                if (kind == MediaServerKind.Emby)
                {
                    Assert.Equal("list", sent.Query["ParentId"]);
                    Assert.Equal("ListItemOrder", sent.Query["SortBy"]);
                    Assert.Equal("Ascending", sent.Query["SortOrder"]);
                }
                Assert.False(sent.Query.ContainsKey("IncludeItemTypes"));
                Assert.Equal(0, Playlist(fixture.Client, "list", 3, 2).Items.Count);
                fixture.Transport.Failure = HttpStatusCode.Forbidden;
                Assert.Throws<EmbyApiException>(() => Playlist(fixture.Client, "list", 0, 2));
            });
        }
        Test("播放列表读取：非法标识和其他后端不发假成员路由", () =>
        {
            using var fixture = new SearchFixture(MediaServerKind.Jellyfin);
            foreach (var id in new[] { "", "../list", "list?token=x", "a/b", "a,b" })
                Assert.Throws<ArgumentException>(() => Playlist(fixture.Client, id, 0, 20));
            foreach (var kind in new[] { MediaServerKind.Plex, MediaServerKind.FnNas })
                Assert.Throws<NotSupportedException>(() => Playlist(new EmbyClient(fixture.Http, Connection(kind)), "list", 0, 20));
            Assert.Equal(0, fixture.Transport.Sent.Count);
        });
    }

    private static LibrarySearchCategory Category(string value)
    {
        Assert.True(Enum.TryParse<LibrarySearchCategory>(value, out var category), "缺少搜索分类 " + value);
        return category;
    }
    private static ItemsResult Search(EmbyClient client, string? term, LibrarySearchCategory category, int start, int limit,
        CancellationToken token = default) => Invoke(client, "SearchItemsAsync", term, category, start, limit, token);
    private static ItemsResult People(EmbyClient client, string? term, int start, int limit,
        CancellationToken token = default) => Invoke(client, "QuerySearchPeopleAsync", term, start, limit, token);
    private static ItemsResult Playlist(EmbyClient client, string id, int start, int limit) =>
        Invoke(client, "GetPlaylistItemsAsync", id, start, limit, CancellationToken.None);
    private static ItemsResult Invoke(EmbyClient client, string name, params object?[] args)
    {
        var method = typeof(EmbyClient).GetMethod(name) ?? throw new AssertionException("缺少真实搜索入口 " + name);
        try { return ((Task<ItemsResult>)method.Invoke(client, args)!).GetAwaiter().GetResult(); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    private static EmbyConnection Connection(MediaServerKind kind) => new(
        new Uri(kind == MediaServerKind.Emby ? "https://search-fixture.invalid/emby/" : "https://search-fixture.invalid/"),
        "placeholder-only", "user", "fixture", "fixture", DeviceIdentity.Create("fixture", "test"))
    { ServerKind = kind, ServerVersion = kind == MediaServerKind.Jellyfin ? "10.10.7" : "4.10.1.0" };

    private sealed class SearchFixture : IDisposable
    {
        internal SearchTransport Transport { get; } = new();
        internal EmbyHttp Http { get; }
        internal EmbyClient Client { get; }
        internal SearchFixture(MediaServerKind kind)
        {
            Http = new EmbyHttp(Transport);
            Client = new(Http, Connection(kind));
        }
        public void Dispose() => Http.Dispose();
    }
    private sealed class SearchTransport : HttpMessageHandler
    {
        internal sealed record Request(Uri Uri, IReadOnlyDictionary<string, string> Query);
        internal List<Request> Sent { get; } = [];
        internal HttpStatusCode? Failure { get; set; }
        internal Action? BeforeReply { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal("search-fixture.invalid", uri.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
            Sent.Add(new(uri, query));
            BeforeReply?.Invoke();
            if (Failure is { } status) return Task.FromResult(new HttpResponseMessage(status));
            var playlist = uri.AbsolutePath == "/Playlists/list/Items" || query.GetValueOrDefault("ParentId") == "list";
            var people = uri.AbsolutePath == "/Persons";
            Assert.True(people || playlist || uri.AbsolutePath is "/emby/Users/user/Items" or "/Users/user/Items" or "/Items", "未登记请求");
            var type = people ? "Person" : query.GetValueOrDefault("IncludeItemTypes")?.Split(',')[0] ?? "Movie";
            var rows = playlist ? new[] { "Z", "A", "M" }.Select(id => new EmbyItem { Id = id, Name = id, Type = "Movie" }).ToList()
                : Enumerable.Range(0, 3).Select(index => new EmbyItem { Id = type + "-" + index, Name = "匿名" + index, Type = type }).ToList();
            var start = int.Parse(query.GetValueOrDefault("StartIndex") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var limit = int.Parse(query["Limit"], System.Globalization.CultureInfo.InvariantCulture);
            // Real old /Persons ignores startIndex and returns only the fetched rows as its total.
            var items = people ? (limit > 0 ? rows.Take(limit).ToList() : rows) : rows.Skip(start).Take(limit).ToList();
            var result = new ItemsResult { Items = items, TotalRecordCount = people ? items.Count : rows.Count };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json") });
        }
    }
}
