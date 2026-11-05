using System.Net;
using System.Text;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LibraryUpcomingTests
{
    public static void Register()
    {
        Test("电视预告：与电影预告片是独立分类和接口", () =>
        {
            Assert.Equal(LibrarySection.Upcoming, LibraryBrowse.Sections("tvshows", MediaServerKind.Emby).Single(entry => entry.Label == "预告").Section);
            Assert.Equal(LibrarySection.Trailers, LibraryBrowse.Sections("movies", MediaServerKind.Emby).Single(entry => entry.Label == "预告片").Section);
            Assert.Equal("Shows/Upcoming", LibraryBrowse.Path(LibrarySection.Upcoming, null, "user"));
            Assert.Equal("Trailers", LibraryBrowse.Path(LibrarySection.Trailers, null, "user"));
            Assert.False(LibraryBrowse.Sections("movies", MediaServerKind.Emby).Any(entry => entry.Section == LibrarySection.Upcoming));
        });
        Test("电视预告：关闭日期显示也保留分组字段且不混入库筛选", () =>
        {
            var filters = new ItemFilters { Genres = ["动画"] };
            var query = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Upcoming, 100, 20,
                EmbySortBy.DateAdded, true, filters, display: new LibraryDisplay { Fields = [] });
            Assert.Equal("library-a", query.ParentId);
            Assert.Equal(100, query.StartIndex);
            Assert.Equal(20, query.Limit);
            Assert.Contains("PremiereDate", query.Fields);
            Assert.Equal("Episode", string.Join(',', query.IncludeItemTypes));
            Assert.Equal("PremiereDate,SortName", query.SortBy);
            Assert.False(query.Descending);
            Assert.Null(query.Filters);
            Assert.Null(query.GroupItemsIntoCollections);
            Assert.Equal("动画", filters.Genres[0]);
        });
        Test("电视预告：日期按顺序分组，同日跨页合并且未知日期排最后", () =>
        {
            var items = new[] { Item("later", "2026-10-19T10:00:00Z"), Item("one", "2026-10-18T12:00:00Z"),
                Item("unknown", null), Item("two", "2026-10-18T14:00:00Z") };
            var groups = LibraryUpcoming.Group(items, TimeZoneInfo.Utc);
            Assert.Equal(3, groups.Count);
            Assert.Equal(new DateOnly(2026, 10, 18), groups[0].Date);
            Assert.Equal("one,two", string.Join(',', groups[0].Items.Select(item => item.Id)));
            Assert.Equal(new DateOnly(2026, 10, 19), groups[1].Date);
            Assert.Null(groups[2].Date);
            Assert.Equal("later", items[0].Id);
            Assert.Equal(0, LibraryUpcoming.Group([], TimeZoneInfo.Utc).Count);
        });
        Test("电视预告：以本地日期分组，午夜和跨年不按 UTC 日错分", () =>
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("fixture-plus-eight", TimeSpan.FromHours(8), "fixture", "fixture");
            var groups = LibraryUpcoming.Group([Item("one", "2026-12-31T18:00:00Z"), Item("two", "2027-01-01T08:00:00+08:00")], zone);
            Assert.Equal(1, groups.Count);
            Assert.Equal(new DateOnly(2027, 1, 1), groups[0].Date);
            Assert.Equal(2, groups[0].Items.Count);
        });
        Test("电视预告：日期标题是昨天或月日星期", () =>
        {
            var today = new DateOnly(2026, 10, 18);
            Assert.Equal("昨天", LibraryUpcoming.DateTitle(today.AddDays(-1), today));
            Assert.Equal("10月18日星期日", LibraryUpcoming.DateTitle(today, today));
            Assert.Equal("10月19日星期一", LibraryUpcoming.DateTitle(today.AddDays(1), today));
            Assert.Equal("10月16日星期五", LibraryUpcoming.DateTitle(today.AddDays(-2), today));
            Assert.Equal("播出日期待定", LibraryUpcoming.DateTitle(null, today));
            Assert.Equal("昨天", LibraryUpcoming.DateTitle(new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1)));
        });
        Test("电视预告：第一行剧名，第二行不补零的季集号和集名", () =>
        {
            var item = new EmbyItem { Name = " 来自远方的恐怖分子 ", SeriesName = " JOJO的奇妙冒险 ", ParentIndexNumber = 6, IndexNumber = 5 };
            Assert.Equal("JOJO的奇妙冒险", LibraryUpcoming.Title(item));
            Assert.Equal("S6:E5 - 来自远方的恐怖分子", LibraryUpcoming.Subtitle(item));
            Assert.Equal(" 来自远方的恐怖分子 ", item.Name);
            item.IndexNumberEnd = 7;
            Assert.Equal("S6:E5–7 - 来自远方的恐怖分子", LibraryUpcoming.Subtitle(item));
            item.SeriesName = null;
            item.ParentIndexNumber = null;
            item.IndexNumberEnd = null;
            Assert.Equal(item.Name, LibraryUpcoming.Title(item));
            Assert.Equal("E5 - 来自远方的恐怖分子", LibraryUpcoming.Subtitle(item));
            item.IndexNumber = null;
            Assert.Equal("来自远方的恐怖分子", LibraryUpcoming.Subtitle(item));
            item.Name = "";
            Assert.Equal("", LibraryUpcoming.Subtitle(item));
            item.ParentIndexNumber = 0;
            item.IndexNumber = 1;
            Assert.Equal("S0:E1", LibraryUpcoming.Subtitle(item));
        });
        Test("电视预告：图片优先单集，其次继承剧图且使用真正所属 ID", () =>
        {
            var item = new EmbyItem
            {
                Id = "episode",
                SeriesId = "show",
                SeriesThumbImageTag = "series-thumb",
                ParentBackdropItemId = "show",
                ParentBackdropImageTags = ["series-backdrop"]
            };
            Assert.Equal(new ArtworkRef("show", "Thumb", "series-thumb"), LibraryUpcoming.Artwork(item));
            item.ImageTags["Primary"] = "episode-still";
            Assert.Equal(new ArtworkRef("episode", "Primary", "episode-still"), LibraryUpcoming.Artwork(item));
            item.ImageTags["Thumb"] = "episode-thumb";
            Assert.Equal(new ArtworkRef("episode", "Thumb", "episode-thumb"), LibraryUpcoming.Artwork(item));
            item.ImageTags.Clear();
            item.SeriesThumbImageTag = null;
            Assert.Equal(new ArtworkRef("show", "Backdrop", "series-backdrop"), LibraryUpcoming.Artwork(item));
            item.ParentBackdropItemId = null;
            item.BackdropImageTags = ["own-backdrop"];
            Assert.Equal(new ArtworkRef("episode", "Backdrop", "own-backdrop"), LibraryUpcoming.Artwork(item));
            item.BackdropImageTags.Clear();
            Assert.Null(LibraryUpcoming.Artwork(item));
        });
        Test("电视预告：宽卡尺寸固定且小窗口不会出界", () =>
        {
            Assert.Equal(248, LibraryUpcoming.WidthFor(1100));
            Assert.Equal(248, LibraryUpcoming.WidthFor(300));
            Assert.Equal(196, LibraryUpcoming.WidthFor(196.5));
            Assert.Equal(248, LibraryUpcoming.WidthFor(double.NaN));
            Assert.Equal(1, LibraryUpcoming.WidthFor(0));
            Assert.Equal(180d, LibraryUpcoming.RowHeight(248));
        });
        Test("电视预告：Emby 请求使用独立接口和播出升序", () => RequestsAsync(MediaServerKind.Emby).GetAwaiter().GetResult());
        Test("电视预告：Jellyfin 请求只带此接口声明的参数", () => RequestsAsync(MediaServerKind.Jellyfin).GetAwaiter().GetResult());
    }

    private static EmbyItem Item(string id, string? date) => new()
    {
        Id = id,
        Name = id,
        Type = EmbyItemType.Episode,
        PremiereDate = date is null ? null : DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture)
    };

    private static async Task RequestsAsync(MediaServerKind kind)
    {
        using var handler = new Transport();
        using var http = new EmbyHttp(handler);
        var client = new EmbyClient(http, new EmbyConnection(new Uri("https://fixture.invalid/media/"), "fixture-token", "user", "User", "Fixture",
            DeviceIdentity.Create("upcoming-fixture", "1"))
        { ServerKind = kind, ServerVersion = "12.2.0" });
        var query = new ItemQuery
        {
            ParentId = "library-a",
            StartIndex = 100,
            Limit = 20,
            Fields = "",
            SortBy = "Random",
            Descending = true,
            Filters = new ItemFilters { Genres = ["动画"] },
            GroupItemsIntoCollections = true
        };
        await client.GetLibraryItemsAsync("tvshows", LibrarySection.Upcoming, null, query, default);
        var uri = handler.Request!;
        Assert.Equal("/media/Shows/Upcoming", uri.AbsolutePath);
        var parameters = uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("library-a", parameters["ParentId"]);
        Assert.Equal("user", parameters["UserId"]);
        Assert.Equal("100", parameters["StartIndex"]);
        Assert.Equal("20", parameters["Limit"]);
        Assert.DoesNotContain("fixture-token", uri.AbsoluteUri);
        foreach (var key in new[] { "Recursive", "IncludeItemTypes", "GroupItemsIntoCollections", "Filters", "Genres", "IsUnaired", "MinPremiereDate" })
            Assert.False(parameters.ContainsKey(key));
        if (kind == MediaServerKind.Emby)
        {
            Assert.Equal("PremiereDate,SortName", parameters["SortBy"]);
            Assert.Equal("Ascending", parameters["SortOrder"]);
            Assert.Contains("PremiereDate", parameters["Fields"]);
        }
        else
        {
            Assert.Equal(8, parameters.Count);
            Assert.Equal("PrimaryImageAspectRatio,Overview", parameters["Fields"]);
            Assert.False(parameters.ContainsKey("SortBy"));
            Assert.False(parameters.ContainsKey("SortOrder"));
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public Uri? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Request = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"Items\":[],\"TotalRecordCount\":0}", Encoding.UTF8, "application/json") });
        }
    }
}
