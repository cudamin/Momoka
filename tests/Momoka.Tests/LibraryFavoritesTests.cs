using System.Net;
using System.Text;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LibraryFavoritesTests
{
    public static void Register()
    {
        Test("电视收藏：节目与集分排，只有支持的电视库采用此版式", () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                var rows = LibraryFavorites.Rows("tvshows", kind);
                Assert.Equal("收藏节目,收藏集", string.Join(',', rows.Select(row => row.Title)));
                Assert.Equal("False,True", string.Join(',', rows.Select(row => row.Wide)));
                Assert.Equal(0, LibraryFavorites.Rows("movies", kind).Count);
                Assert.Equal(0, LibraryFavorites.Rows(null, kind).Count);
            }
            Assert.Equal(0, LibraryFavorites.Rows("tvshows", MediaServerKind.FnNas).Count);
            Assert.Equal(0, LibraryFavorites.Rows("tvshows", MediaServerKind.Plex).Count);
        });
        Test("电视收藏：两排只问本库中真实收藏的对应类型", () =>
        {
            foreach (var shelf in Enum.GetValues<FavoriteShelf>())
            {
                var query = LibraryFavorites.Query("library-a", shelf);
                Assert.Equal("library-a", query.ParentId);
                Assert.True(query.Recursive);
                Assert.True(query.IsFavorite);
                Assert.Equal(shelf == FavoriteShelf.Shows ? "Series" : "Episode", string.Join(',', query.IncludeItemTypes));
                Assert.Equal("IsFavorite", query.ToParameters().Single(pair => pair.Key == "Filters").Value);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => LibraryFavorites.Query("library-a", (FavoriteShelf)99));
        });
        Test("电视收藏：每排有独立分页位置，不先混查再本地拆排", () =>
        {
            var shows = LibraryFavorites.Query("library-a", FavoriteShelf.Shows, 0, 12);
            var episodes = LibraryFavorites.Query("library-a", FavoriteShelf.Episodes, 100, 40);
            Assert.Equal(0, shows.StartIndex);
            Assert.Equal(12, shows.Limit);
            Assert.Equal(100, episodes.StartIndex);
            Assert.Equal(40, episodes.Limit);
            Assert.Equal(LibraryFavorites.PageSize, LibraryFavorites.Query("library-a", FavoriteShelf.Shows).Limit);
            Assert.Equal("Series", shows.IncludeItemTypes.Single());
            Assert.Equal("Episode", episodes.IncludeItemTypes.Single());
        });
        Test("电视收藏：固定标题排序，不携带其它分类的筛选或合集分组", () =>
        {
            var query = LibraryFavorites.Query("library-a", FavoriteShelf.Episodes);
            Assert.Equal(EmbySortBy.Name, query.SortBy);
            Assert.False(query.Descending);
            Assert.Null(query.Filters);
            Assert.Null(query.GroupItemsIntoCollections);
            Assert.DoesNotContain("Genres=", query.ToQueryString());
            Assert.DoesNotContain("GroupItemsIntoCollections", query.ToQueryString());
        });
        Test("电视收藏：请求保留连载状态与结束年份字段", () =>
        {
            var query = LibraryFavorites.Query("library-a", FavoriteShelf.Shows);
            Assert.Equal(EmbyFields.Browse, query.Fields);
            Assert.Contains("ProductionYear", query.Fields);
            Assert.Contains("EndDate", query.Fields);
            Assert.Contains("Status", query.Fields);
        });
        Test("电视收藏：节目卡写年份区间和现在，不写季数", () =>
        {
            var series = new EmbyItem { Type = EmbyItemType.Series, Name = "碧蓝之海", ProductionYear = 2018, Status = "Continuing" };
            Assert.Equal("碧蓝之海", LibraryFavorites.Title(series));
            Assert.Equal("2018 - 现在", LibraryFavorites.Subtitle(series));
            Assert.Equal("2016 - 2018", LibraryFavorites.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Series,
                ProductionYear = 2016,
                Status = "Ended",
                EndDate = new DateTimeOffset(2018, 6, 1, 0, 0, 0, TimeSpan.Zero)
            }));
            Assert.Equal("2018", LibraryFavorites.Subtitle(new EmbyItem { Type = EmbyItemType.Series, ProductionYear = 2018 }));
            Assert.Null(LibraryFavorites.Subtitle(new EmbyItem { Type = EmbyItemType.Series }));
        });
        Test("电视收藏：单集卡第一行剧名，第二行季集号和集名", () =>
        {
            var episode = new EmbyItem
            {
                Type = EmbyItemType.Episode,
                Name = "败北",
                SeriesName = "BORDER 警视厅搜查一课",
                ParentIndexNumber = 1,
                IndexNumber = 7
            };
            Assert.Equal("BORDER 警视厅搜查一课", LibraryFavorites.Title(episode));
            Assert.Equal("S1:E7 - 败北", LibraryFavorites.Subtitle(episode));
            Assert.Equal("败北", LibraryFavorites.Title(new EmbyItem { Type = EmbyItemType.Episode, Name = "败北" }));
            Assert.Equal("E7–8 - 败北", LibraryFavorites.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Episode,
                Name = "败北",
                IndexNumber = 7,
                IndexNumberEnd = 8
            }));
        });
        Test("电视收藏：缺少季集号或集名不留下孤立分隔符", () =>
        {
            Assert.Equal("败北", LibraryFavorites.Subtitle(new EmbyItem { Type = EmbyItemType.Episode, Name = " 败北 " }));
            Assert.Equal("S0:E7", LibraryFavorites.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Episode,
                Name = "",
                ParentIndexNumber = 0,
                IndexNumber = 7
            }));
            Assert.Equal("", LibraryFavorites.Subtitle(new EmbyItem { Type = EmbyItemType.Episode, Name = "" }));
            Assert.Null(LibraryFavorites.Subtitle(new EmbyItem { Type = EmbyItemType.Movie }));
        });
        Test("电视收藏：卡片保持网页尺寸，窄窗不足时缩小", () =>
        {
            Assert.Equal(148, LibraryFavorites.WidthFor(false, 1100));
            Assert.Equal(216, LibraryFavorites.WidthFor(true, 1100));
            Assert.Equal(120, LibraryFavorites.WidthFor(true, 120));
            Assert.Equal(1, LibraryFavorites.WidthFor(false, -10));
            Assert.Equal(216, LibraryFavorites.WidthFor(true, double.NaN));
            Assert.Equal(262d, LibraryFavorites.RowHeight(148, false));
            Assert.Equal(162d, LibraryFavorites.RowHeight(216, true));
        });
        Test("媒体库弹窗：比旧宽度大，宽高随可用窗口改变且不越界", () =>
        {
            var medium = LibraryPanelSize.For(1060, 760);
            var large = LibraryPanelSize.For(1460, 960);
            var narrow = LibraryPanelSize.For(320, 500);
            Assert.True(medium.Width > 520);
            Assert.True(large.Width > medium.Width && large.Height > medium.Height);
            Assert.Equal((296d, 476d), narrow);
            Assert.True(medium.Width <= 1036 && medium.Height <= 736);
            Assert.Equal(1000d, LibraryPanelSize.For(3800, 2000).Width);
            Assert.Equal((0d, 0d), LibraryPanelSize.For(double.NaN, double.PositiveInfinity));
        });
        Test("电视收藏：Emby 两排实际请求限定当前库，令牌只在头部", () => RequestsAsync(MediaServerKind.Emby, "4.10.0.40").GetAwaiter().GetResult());
        Test("电视收藏：Jellyfin 12 两排使用新路由与合法字段", () => RequestsAsync(MediaServerKind.Jellyfin, "12.2.0").GetAwaiter().GetResult());
        Test("电视收藏：Jellyfin 旧版两排保留用户作用域路由", () => RequestsAsync(MediaServerKind.Jellyfin, "10.11.0").GetAwaiter().GetResult());
    }

    private static async Task RequestsAsync(MediaServerKind kind, string version)
    {
        using var handler = new Transport(kind);
        using var http = new EmbyHttp(handler);
        var client = new EmbyClient(http,
            new EmbyConnection(new Uri("https://fixture.invalid/media/"), "fixture-token", "user", "User", "Fixture",
                DeviceIdentity.Create("library-favorites-fixture", "1"))
            { ServerKind = kind, ServerVersion = version });
        foreach (var row in LibraryFavorites.Rows("tvshows", kind))
        {
            await client.GetLibraryItemsAsync("tvshows", LibrarySection.Favorites, null,
                LibraryFavorites.Query("library-a", row.Shelf, 20, 40), default);
            var uri = handler.Requests.Last();
            var query = Uri.UnescapeDataString(uri.Query);
            var modern = kind == MediaServerKind.Jellyfin && version.StartsWith("12.", StringComparison.Ordinal);
            Assert.Equal(modern ? "/media/Items" : "/media/Users/user/Items", uri.AbsolutePath);
            Assert.Contains("ParentId=library-a", query);
            Assert.Contains("Filters=IsFavorite", query);
            Assert.Contains("IncludeItemTypes=" + (row.Wide ? "Episode" : "Series"), query);
            Assert.Contains("StartIndex=20", query);
            Assert.Contains("Limit=40", query);
            Assert.DoesNotContain("fixture-token", uri.AbsoluteUri);
            Assert.DoesNotContain("GroupItemsIntoCollections", query);
            if (kind == MediaServerKind.Jellyfin)
            {
                var fields = query.TrimStart('?').Split('&').Single(part => part.StartsWith("Fields=", StringComparison.Ordinal));
                Assert.DoesNotContain("EndDate", fields);
                Assert.DoesNotContain("Status", fields);
                Assert.DoesNotContain("ProductionYear", fields);
                if (modern) Assert.Contains("UserId=user", query);
            }
        }
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class Transport(MediaServerKind kind) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            if (kind == MediaServerKind.Emby)
                Assert.Equal("fixture-token", request.Headers.GetValues("X-Emby-Token").Single());
            else
                Assert.Contains("fixture-token", request.Headers.GetValues("Authorization").Single());
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"Items\":[],\"TotalRecordCount\":0}", Encoding.UTF8, "application/json")
            });
        }
    }
}
