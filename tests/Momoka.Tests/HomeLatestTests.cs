using Momoka.Configuration;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页「最近添加」的取数口径：Jellyfin 的剧集库不能走 <c>/Items/Latest</c>（只有剧集、没有单集的库会回空）。
/// 判据和查询形状都在 <see cref="HomeLatest"/>，这里钉住它，免得哪天为了「统一」又把这半句删掉。
/// </summary>
internal static class HomeLatestTests
{
    public static void Register()
    {
        Test("主页最近添加：Jellyfin 剧集库改走剧集条目查询", () =>
        {
            Assert.True(HomeLatest.UsesSeriesQuery(Library(MediaServerKind.Jellyfin, "tvshows")), "Jellyfin 剧集库应改走剧集查询");
            Assert.True(HomeLatest.UsesSeriesQuery(Library(MediaServerKind.Jellyfin, "TVShows")), "集合类型不应区分大小写");
        });

        Test("主页最近添加：其它服务器与其它库维持 /Items/Latest", () =>
        {
            Assert.False(HomeLatest.UsesSeriesQuery(Library(MediaServerKind.Emby, "tvshows")), "Emby 不需要改道");
            Assert.False(HomeLatest.UsesSeriesQuery(Library(MediaServerKind.Jellyfin, "movies")), "Jellyfin 电影库不需要改道");
            Assert.False(HomeLatest.UsesSeriesQuery(Library(MediaServerKind.Jellyfin, null)), "没有集合类型时不猜");
            Assert.False(HomeLatest.UsesSeriesQuery(new EmbyItem { Id = "7", CollectionType = "tvshows" }), "没有来源时不改道");
        });

        Test("主页最近添加：剧集查询按最近入库内容倒序列剧集", () =>
        {
            var query = HomeLatest.SeriesQuery("library-7", 24);

            Assert.Equal("library-7", query.ParentId);
            Assert.True(query.Recursive, "要递归到库内剧集");
            Assert.Equal(EmbyItemType.Series, query.IncludeItemTypes.Single());
            Assert.Equal("DateLastContentAdded,SortName", query.SortBy);
            Assert.True(query.Descending, "最近添加要新的在前");
            Assert.Equal(24, query.Limit);
            Assert.Null(query.IsPlayed);
        });

        // 2026-10-25 用户令「飞牛影视库不要再显示单集封面」：飞牛的 item/list 没有 GroupItems，不带类型时
        // 会把剧集和单集一起发回来，货架上于是并排摆着剧和它自己的某一集。
        Test("主页最近添加：飞牛货架按库的类型问，其余服务器不加类型", () =>
        {
            Assert.Equal("Series,Video", HomeLatest.LibraryTypes(Library(MediaServerKind.FnNas, "tvshows")));
            // BoxSet 飞牛没有，请求那一头会把它丢掉（FnNasMediaAdapter.Query），这里只钉「按库的类型问」。
            Assert.Equal("Movie,Video,BoxSet", HomeLatest.LibraryTypes(Library(MediaServerKind.FnNas, "movies")));
            Assert.Equal("Movie,Series,Video", HomeLatest.LibraryTypes(Library(MediaServerKind.FnNas, "mixed")));
            Assert.Null(HomeLatest.LibraryTypes(Library(MediaServerKind.FnNas, null)), "没有集合类型时不猜");
            Assert.Null(HomeLatest.LibraryTypes(Library(MediaServerKind.Emby, "tvshows")), "Emby 照旧不加类型");
            Assert.Null(HomeLatest.LibraryTypes(Library(MediaServerKind.Jellyfin, "tvshows")));
            Assert.Null(HomeLatest.LibraryTypes(new EmbyItem { Id = "7", CollectionType = "tvshows" }), "没有来源时不加类型");
        });
    }

    private static EmbyItem Library(MediaServerKind kind, string? collectionType)
    {
        var connection = new EmbyConnection(new Uri("https://fixture.invalid/"), "fixture", "user", "user",
            "fixture", DeviceIdentity.Create("home-latest-fixture", "test"))
        {
            ServerKind = kind
        };
        var scope = new EmbySessionScope(null!, new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = "server", Name = "fixture" },
            new AccountProfile { Id = "account", Username = "user" }));
        return new EmbyItem { Id = "library-7", CollectionType = collectionType, Source = scope };
    }
}
