using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 媒体库设置面板背后的那几件事：字段目录、按库记住的一份设置、请求参数，以及卡片上那两行字。
/// <para>
/// 这里最要紧的一条是**电视剧和电影不是同一套说法**：剧集的「年份」是区间、「播放时长」是每集多长，
/// 电影的是单值和整片。两种库共用同一份字段目录，但落到屏上的字不一样 —— 这一条单独钉住。
/// </para>
/// </summary>
internal static class LibraryDisplayTests
{
    public static void Register()
    {
        Test("媒体库设置：字段目录与网页端十四项一致", () =>
        {
            Assert.Equal("标题,IMDb 评分,影评人评分,家长评分,年份,播放时长,类型,导演,标签,工作室,宣传语,概要,播放日期,创建日期",
                string.Join(',', LibraryDisplayCatalog.Fields.Select(info => info.Label)));
            Assert.Equal(14, LibraryDisplayCatalog.Fields.Count);
        });
        Test("媒体库设置：字段要问服务器的名字照抄 ItemFields", () =>
        {
            Assert.Equal("People", LibraryDisplayCatalog.Info(LibraryField.Director).RequestField);
            Assert.Equal("Taglines", LibraryDisplayCatalog.Info(LibraryField.Tagline).RequestField);
            Assert.Equal<string?>(null, LibraryDisplayCatalog.Info(LibraryField.Title).RequestField);
            Assert.Equal<string?>(null, LibraryDisplayCatalog.Info(LibraryField.Runtime).RequestField);

            var asked = LibraryDisplayCatalog.RequestFields([LibraryField.Title, LibraryField.Director, LibraryField.Overview]);
            Assert.Equal("People,Overview,PrimaryImageAspectRatio", asked);
            Assert.DoesNotContain("CommunityRating", asked);
        });
        Test("媒体库设置：一条都没勾也要留下卡片底座", () =>
        {
            Assert.Equal("PrimaryImageAspectRatio", LibraryDisplayCatalog.RequestFields([]));
        });
        Test("媒体库设置：认不出的字段与重复项被整理掉，顺序按目录", () =>
        {
            var display = new LibraryDisplay
            {
                View = (LibraryView)99,
                CardSize = (LibraryCardSize)99,
                Fields = [LibraryField.Studios, LibraryField.Title, (LibraryField)77, LibraryField.Studios]
            }.Normalized();

            Assert.Equal(LibraryView.Poster, display.View);
            Assert.Equal(LibraryCardSize.Default, display.CardSize);
            Assert.Equal("Title,Studios", string.Join(',', display.Fields));
            Assert.True(display.GroupIntoCollections);
        });
        Test("媒体库设置：八档图像大小逐档变宽，默认与中同宽", () =>
        {
            var widths = Enum.GetValues<LibraryCardSize>()
                .Select(size => LibraryCardMetrics.Width(LibraryView.Poster, size)).ToArray();
            Assert.Equal(170, LibraryCardMetrics.Width(LibraryView.Poster, LibraryCardSize.Default));
            Assert.Equal(widths[(int)LibraryCardSize.Normal], widths[(int)LibraryCardSize.Default]);
            for (var index = 1; index < widths.Length; index++)
            {
                // 「中」与「默认」是同一档（网页端默认跟随应用默认，这个客户端没有第二套默认），
                // 其余每一档都要比上一档宽。
                var same = (LibraryCardSize)index is LibraryCardSize.Default;
                Assert.True(same ? widths[index] == widths[index - 1] : widths[index] > widths[index - 1],
                    $"第 {index} 档没有比上一档宽");
            }
        });
        Test("媒体库设置：七种视图各有各的高宽比", () =>
        {
            Assert.Equal(255, LibraryCardMetrics.Height(LibraryView.Poster, 170));
            Assert.Equal(170, LibraryCardMetrics.Height(LibraryView.Disc, 170));
            Assert.Equal(56, LibraryCardMetrics.Height(LibraryView.Banner, 300));
            Assert.Equal(169, LibraryCardMetrics.Height(LibraryView.Thumb, 300));
            Assert.Equal(169, LibraryCardMetrics.Height(LibraryView.Logo, 300));
        });
        Test("媒体库设置：分组只发给库内的条目查询，推荐不认这一键", () =>
        {
            var on = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Items, 0, 20,
                EmbySortBy.Name, false, new(), null, new LibraryDisplay());
            Assert.Contains("GroupItemsIntoCollections=true", on.ToQueryString());

            var off = LibraryBrowse.Query("movies", "library-b", LibrarySection.Items, 0, 20,
                EmbySortBy.Name, false, new(), null, new LibraryDisplay { GroupIntoCollections = false });
            Assert.Contains("GroupItemsIntoCollections=false", off.ToQueryString());

            var recommended = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Recommended, 0, 20,
                EmbySortBy.Name, false, new(), null, new LibraryDisplay());
            Assert.DoesNotContain("GroupItemsIntoCollections", recommended.ToQueryString());
        });
        Test("媒体库设置：查询只带勾上的字段", () =>
        {
            var display = new LibraryDisplay { Fields = [LibraryField.Title, LibraryField.Studios] };
            var query = LibraryBrowse.Query("movies", "library-a", LibrarySection.Items, 0, 20,
                EmbySortBy.Name, false, new(), null, display);
            Assert.Contains("Fields=Studios%2CPrimaryImageAspectRatio", query.ToQueryString());
            Assert.DoesNotContain("People", query.ToQueryString());
        });
        Test("媒体库设置：电影与剧集的年份说法不同", () =>
        {
            var movie = Item(EmbyItemType.Movie, year: 2019);
            var series = Item(EmbyItemType.Series, year: 2017, endYear: 2021);
            var running = Item(EmbyItemType.Series, year: 2017, status: "Continuing");

            Assert.Equal("2019", LibraryCardText.Line(movie, [LibraryField.ProductionYear]));
            Assert.Equal("2017 – 2021", LibraryCardText.Line(series, [LibraryField.ProductionYear]));
            Assert.Equal("2017 – 至今", LibraryCardText.Line(running, [LibraryField.ProductionYear]));
        });
        Test("媒体库设置：电影时长是整片，剧集时长是每集", () =>
        {
            var movie = Item(EmbyItemType.Movie, year: 2019, minutes: 132);
            var series = Item(EmbyItemType.Series, year: 2017, minutes: 45);

            Assert.Equal("2 小时 12 分", LibraryCardText.Line(movie, [LibraryField.Runtime]));
            Assert.Equal("每集 45 分钟", LibraryCardText.Line(series, [LibraryField.Runtime]));
        });
        Test("媒体库设置：卡片一行按字段顺序拼，单集带集号", () =>
        {
            var movie = Item(EmbyItemType.Movie, year: 2019, minutes: 132);
            movie.CommunityRating = 8.1f;
            movie.Genres = ["剧情", "科幻", "悬疑"];
            movie.Studios = [new EmbyStudio { Name = "A24" }];
            movie.People = [new EmbyPerson { Name = "丹尼斯·维伦纽瓦", Type = "Director" }];

            var line = LibraryCardText.Line(movie,
                [LibraryField.Title, LibraryField.CommunityRating, LibraryField.ProductionYear,
                 LibraryField.Runtime, LibraryField.Genres, LibraryField.Director, LibraryField.Studios]);
            Assert.Equal("8.1  ·  2019  ·  2 小时 12 分  ·  剧情 / 科幻  ·  丹尼斯·维伦纽瓦  ·  A24", line);

            var episode = Item(EmbyItemType.Episode, year: 2016, minutes: 24);
            episode.ParentIndexNumber = 2;
            episode.IndexNumber = 5;
            Assert.Contains("S02E05", LibraryCardText.Line(episode, [LibraryField.Title, LibraryField.ProductionYear]));
        });
        Test("媒体库设置：宣传语优先于概要，两样都没勾就没有第三行", () =>
        {
            var movie = Item(EmbyItemType.Movie, year: 2019);
            movie.Taglines = ["一个人的战争"];
            movie.Overview = "简介正文";

            Assert.Equal("一个人的战争", LibraryCardText.Note(movie, [LibraryField.Overview, LibraryField.Tagline]));
            Assert.Equal("简介正文", LibraryCardText.Note(movie, [LibraryField.Overview]));
            Assert.Equal<string?>(null, LibraryCardText.Note(movie, [LibraryField.Title]));
        });
        Test("媒体库设置：表格的每一格与列头同一份宽度", () =>
        {
            var movie = Item(EmbyItemType.Movie, year: 2019);
            Assert.Equal("2019", LibraryCardText.Cell(movie, LibraryField.ProductionYear));
            Assert.Equal("科幻", LibraryCardText.Cell(Item(EmbyItemType.Movie, year: 2020, genre: "科幻"), LibraryField.Genres));
            Assert.Equal(260, LibraryTableColumns.WidthOf(LibraryField.Title));
            Assert.True(LibraryTableColumns.WidthOf(LibraryField.Overview) > LibraryTableColumns.WidthOf(LibraryField.ProductionYear));
        });
        Test("媒体库设置：一份设置存得住、读得回", () =>
        {
            var display = new LibraryDisplay
            {
                View = LibraryView.Table,
                CardSize = LibraryCardSize.Larger,
                GroupIntoCollections = false,
                Fields = [LibraryField.Title, LibraryField.Genres, LibraryField.Studios]
            };

            var json = System.Text.Json.JsonSerializer.Serialize(display);
            var back = System.Text.Json.JsonSerializer.Deserialize<LibraryDisplay>(json)!;

            Assert.Equal(LibraryView.Table, back.View);
            Assert.Equal(LibraryCardSize.Larger, back.CardSize);
            Assert.False(back.GroupIntoCollections);
            Assert.Equal("Title,Genres,Studios", string.Join(',', back.Fields));
        });
    }

    private static EmbyItem Item(string type, int year, int minutes = 0, int? endYear = null, string? status = null,
        string? genre = null) => new()
        {
            Id = "item-" + type,
            Name = "条目",
            Type = type,
            ProductionYear = year,
            PremiereDate = new DateTimeOffset(year, 5, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = endYear is { } until ? new DateTimeOffset(until, 5, 1, 0, 0, 0, TimeSpan.Zero) : null,
            Status = status,
            RunTimeTicks = minutes * 60L * 10_000_000L,
            Genres = genre is null ? [] : [genre]
        };
}
