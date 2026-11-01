using Momoka.Configuration;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页库排右端读数的取数口径：牌子写的是库一共有多少项，不是货架装了几张卡（上限 24）。口径跟点进库后
/// 第一页的「共 N 项」一致，查询形状钉在 <see cref="LibraryTotal"/>。
/// </summary>
internal static class LibraryTotalTests
{
    public static void Register()
    {
        Test("库排读数：Emby 按库页第一页的类型递归数", () =>
        {
            var movies = LibraryTotal.Query(Library(MediaServerKind.Emby, "movies"));
            Assert.Equal("library-7", movies!.ParentId);
            Assert.True(movies.Recursive, "电影库要递归到库内影片");
            Assert.Equal("Movie,Video", string.Join(',', movies.IncludeItemTypes));
            Assert.Equal(1, movies.Limit);
            Assert.Equal("", movies.Fields);

            var shows = LibraryTotal.Query(Library(MediaServerKind.Emby, "tvshows"))!;
            Assert.Equal("Series,Video", string.Join(',', shows.IncludeItemTypes));

            var mixed = LibraryTotal.Query(Library(MediaServerKind.Emby, "mixed"))!;
            Assert.Equal("Movie,Series,Video", string.Join(',', mixed.IncludeItemTypes));
        });

        Test("库排读数：Jellyfin 普通库与 Emby 同一份类型表", () =>
        {
            var movies = LibraryTotal.Query(Library(MediaServerKind.Jellyfin, "movies"))!;
            Assert.True(movies.Recursive);
            Assert.Equal("Movie,Video", string.Join(',', movies.IncludeItemTypes));

            var shows = LibraryTotal.Query(Library(MediaServerKind.Jellyfin, "tvshows"))!;
            Assert.Equal("Series,Video", string.Join(',', shows.IncludeItemTypes));
        });

        Test("库排读数：Jellyfin 的 list 页数直接子项", () =>
        {
            foreach (var collectionType in new[] { "mixed", "folders", null })
            {
                var query = LibraryTotal.Query(Library(MediaServerKind.Jellyfin, collectionType))!;
                Assert.False(query.Recursive, $"Jellyfin list 页（{collectionType ?? "null"}）不该递归");
                Assert.Equal(0, query.IncludeItemTypes.Count);
                Assert.Equal(1, query.Limit);
            }
        });

        Test("库排读数：飞牛、Plex、音乐库与无来源不问总数", () =>
        {
            Assert.Null(LibraryTotal.Query(Library(MediaServerKind.FnNas, "tvshows")), "飞牛走 mediadb/sum");
            Assert.Null(LibraryTotal.Query(Library(MediaServerKind.Plex, "movies")), "Plex 尚未核对");
            Assert.Null(LibraryTotal.Query(Library(MediaServerKind.Emby, "music")), "音乐库主页不展示，不为它多问一趟");
            Assert.Null(LibraryTotal.Query(new EmbyItem { Id = "7", CollectionType = "movies" }), "没有来源时不猜产品");
        });
    }

    private static EmbyItem Library(MediaServerKind kind, string? collectionType)
    {
        var connection = new EmbyConnection(new Uri("https://fixture.invalid/"), "fixture", "user", "user",
            "fixture", DeviceIdentity.Create("library-total-fixture", "test"))
        {
            ServerKind = kind
        };
        var scope = new EmbySessionScope(null!, new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = "server", Name = "fixture" },
            new AccountProfile { Id = "account", Username = "user" }));
        return new EmbyItem { Id = "library-7", CollectionType = collectionType, Source = scope };
    }
}
