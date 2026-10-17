using System.Net;
using System.Text;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LibraryBrowseTests
{
    public static void Register()
    {
        Test("媒体库分类：电影与电视剧按网页顺序分别显示", () =>
        {
            Assert.Equal("影片,推荐,预告片,合集,类型,收藏,文件夹", Labels("movies", MediaServerKind.Emby));
            Assert.Equal("节目,推荐,预告,收藏,类型,播出平台,集,文件夹", Labels("tvshows", MediaServerKind.Emby));
            Assert.Equal(0, LibraryBrowse.Sections("music", MediaServerKind.Emby).Count);
        });
        Test("媒体库分类：不支持的服务端入口隐藏", () =>
        {
            Assert.DoesNotContain("推荐", Labels("tvshows", MediaServerKind.Jellyfin));
            Assert.Equal("影片,收藏", Labels("movies", MediaServerKind.FnNas));
            Assert.Equal("节目", Labels("tvshows", MediaServerKind.Plex));
        });
        Test("媒体库分类：所有查询保留库作用域和分页", () =>
        {
            foreach (var section in Enum.GetValues<LibrarySection>())
            {
                var query = Query(section);
                Assert.Equal("library-a", query.ParentId);
                Assert.Equal(20, query.StartIndex);
                Assert.Equal(20, query.Limit);
                Assert.Equal(section != LibrarySection.Folders, query.Recursive);
            }
        });
        Test("媒体库分类：单集、合集、预告、文件夹分别查询", () =>
        {
            Assert.Equal("Episode", string.Join(',', Query(LibrarySection.Episodes).IncludeItemTypes));
            Assert.Equal("BoxSet", string.Join(',', Query(LibrarySection.Collections).IncludeItemTypes));
            Assert.Equal("Trailer", string.Join(',', Query(LibrarySection.Trailers).IncludeItemTypes));
            Assert.Equal(0, Query(LibrarySection.Folders).IncludeItemTypes.Count);
            Assert.False(LibraryBrowse.Query("movies", "library-a", LibrarySection.Items, 0, 20,
                EmbySortBy.Name, false, new()).IncludeItemTypes.Contains(EmbyItemType.BoxSet));
        });
        Test("媒体库分类：收藏为不可清除的固定条件", () =>
        {
            var filters = new ItemFilters();
            Assert.True(Query(LibrarySection.Favorites, filters: filters).IsFavorite);
            Assert.Contains("Filters=IsFavorite", Query(LibrarySection.Favorites, filters: filters).ToQueryString());
        });
        Test("媒体库分类：类型与平台索引不携带内容筛选", () =>
        {
            var filters = new ItemFilters { Genres = ["科幻"] };
            foreach (var section in new[] { LibrarySection.Genres, LibrarySection.Studios })
            {
                var query = Query(section, filters: filters);
                Assert.True(query.Filters!.IsBlank);
                Assert.Equal(EmbySortBy.Name, query.SortBy);
                Assert.False(query.Descending);
            }
            Assert.Equal("科幻", filters.Genres[0]);
        });
        Test("媒体库分类：类型与播出平台下钻仍限定同一库", () =>
        {
            var genre = Query(LibrarySection.Genres, "动画 / 科幻");
            var studio = Query(LibrarySection.Studios, "TBS & Tokyo");
            Assert.Equal("library-a", genre.ParentId);
            Assert.Equal("动画 / 科幻", genre.Genre);
            Assert.Equal("TBS & Tokyo", studio.Studio);
            Assert.Contains("Studios=TBS%20%26%20Tokyo", studio.ToQueryString());
            Assert.False(LibraryBrowse.IsIndex(LibrarySection.Studios, "TBS & Tokyo"));
        });
        Test("媒体库分类：推荐不覆盖服务端推荐顺序", () =>
        {
            Assert.DoesNotContain("SortBy", Query(LibrarySection.Recommended).ToQueryString());
        });
        Test("媒体库分类：排序菜单跟随当前内容类型", () =>
        {
            Assert.Equal(EmbySortPreset.Episode, LibraryBrowse.SortPreset("tvshows", LibrarySection.Episodes));
            Assert.Equal(EmbySortPreset.BoxSet, LibraryBrowse.SortPreset("movies", LibrarySection.Collections));
            Assert.Equal(EmbySortPreset.Trailer, LibraryBrowse.SortPreset("tvshows", LibrarySection.Trailers));
            Assert.Equal<string?>(null, LibraryBrowse.SortPreset("movies", LibrarySection.Folders));
        });
        Test("媒体库分类：Emby 实际请求路径、作用域、正文与凭据边界", () => RequestsAsync(MediaServerKind.Emby).GetAwaiter().GetResult());
        Test("媒体库分类：Jellyfin 新路由、类型和平台保持作用域", () => RequestsAsync(MediaServerKind.Jellyfin).GetAwaiter().GetResult());
        Test("媒体库分类：Plex 不支持的入口不发网络请求", () =>
        {
            using var handler = new Transport();
            using var http = new EmbyHttp(handler);
            var client = Client(http, MediaServerKind.Plex);
            Assert.Throws<NotSupportedException>(() => client.GetLibraryItemsAsync("movies", LibrarySection.Collections,
                null, Query(LibrarySection.Collections), default));
            Assert.Equal(0, handler.Requests.Count);
        });
    }

    private static string Labels(string type, MediaServerKind kind) =>
        string.Join(',', LibraryBrowse.Sections(type, kind).Select(entry => entry.Label));
    private static ItemQuery Query(LibrarySection section, string? group = null, ItemFilters? filters = null) =>
        LibraryBrowse.Query("tvshows", "library-a", section, 20, 20, EmbySortBy.DateAdded, true, filters ?? new(), group);
    private static EmbyClient Client(EmbyHttp http, MediaServerKind kind) => new(http,
        new EmbyConnection(new Uri("https://fixture.invalid/media/"), "fixture-token", "user", "User", "Fixture",
            DeviceIdentity.Create("library-fixture", "1"))
        { ServerKind = kind, ServerVersion = "12.2.0" });

    private static async Task RequestsAsync(MediaServerKind kind)
    {
        using var handler = new Transport();
        using var http = new EmbyHttp(handler);
        var client = Client(http, kind);
        foreach (var entry in LibraryBrowse.Sections("tvshows", kind))
        {
            await client.GetLibraryItemsAsync("tvshows", entry.Section, null, Query(entry.Section), default);
            var uri = handler.Requests.Last();
            Assert.Contains("ParentId=library-a", uri.Query);
            Assert.DoesNotContain("fixture-token", uri.AbsoluteUri);
            var path = LibraryBrowse.Path(entry.Section, null, "user");
            if (kind == MediaServerKind.Jellyfin && path == "Users/user/Items") path = "Items";
            Assert.Equal("/media/" + path, uri.AbsolutePath);
        }
        await client.GetLibraryItemsAsync("tvshows", LibrarySection.Studios, "TBS & Tokyo", Query(LibrarySection.Studios, "TBS & Tokyo"), default);
        Assert.Contains("Studios=TBS%20%26%20Tokyo", handler.Requests.Last().Query);
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal<HttpContent?>(null, request.Content);
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"Items\":[],\"TotalRecordCount\":0}", Encoding.UTF8, "application/json") });
        }
    }
}
