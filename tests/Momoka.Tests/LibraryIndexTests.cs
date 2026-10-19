using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 电视节目「类型」与「播出平台」两个索引分区（2026-10-18 用户令：照网页端复刻）。
/// <para>
/// 这两页的规则有几处**只看屏幕看不出来**：卡片该是方还是 16:9、取哪一张图、底下有几行字、设置面板
/// 摆哪几行。写错的时候屏上照样是一堆卡片，只是图裁掉一截、或者名字下面多出一行英文 —— 所以每一条都
/// 在这里钉住。
/// </para>
/// <para>
/// 依据分三层：网页端 <c>cardbuilder</c>／<c>imagehelper</c> 的取图与形状规则；本机那台 4.10.1.0 上实测的
/// 数据（22 个类型里 8 个有 1:1 的 <c>Primary</c>；134 个台里 23 个有 16:9 的 <c>Thumb</c>）；以及用户给的
/// 网页端截图。没有在内网库上点过这两页（见 PROGRESS）。
/// </para>
/// </summary>
internal static class LibraryIndexTests
{
    public static void Register()
    {
        Test("索引页：只有没下钻的那一层算索引页，下钻回去就成了普通一页", () =>
        {
            Assert.Equal(LibraryIndexKind.Genre, LibraryBrowse.IndexKind(LibrarySection.Genres, null));
            Assert.Equal(LibraryIndexKind.Studio, LibraryBrowse.IndexKind(LibrarySection.Studios, null));

            // 点进「类型 → 动画」之后 Section 还是 Genres，但那一页装的是剧集：卡片要退回普通海报，
            // 字段表也要退回十四项。只按 Section 分会把这一页也当成索引页。
            Assert.Equal(LibraryIndexKind.None, LibraryBrowse.IndexKind(LibrarySection.Genres, "动画"));
            Assert.Equal(LibraryIndexKind.None, LibraryBrowse.IndexKind(LibrarySection.Studios, "AT-X"));

            Assert.Equal(LibraryIndexKind.None, LibraryBrowse.IndexKind(LibrarySection.Items, null));
            Assert.Equal(LibraryIndexKind.None, LibraryBrowse.IndexKind(LibrarySection.Episodes, null));
            Assert.False(LibraryBrowse.IsIndex(LibrarySection.Genres, "动画"));
            Assert.True(LibraryBrowse.IsIndex(LibrarySection.Genres, null));
        });
        Test("索引页：类型是方卡、播出平台是 16:9 宽卡，八档图像大小各自缩放", () =>
        {
            // 类型的图是服务器按这个类型拼出来的四格封面（本机实测 8 个有图的类型全是 1:1）；
            // 播出平台的图是台标，服务器存在 Thumb 里、全是 16:9。
            Assert.Equal(170, LibraryCardMetrics.Width(LibraryView.Poster, LibraryCardSize.Default,
                LibrarySection.Genres, LibraryIndexKind.Genre));
            Assert.Equal(300, LibraryCardMetrics.Width(LibraryView.Poster, LibraryCardSize.Default,
                LibrarySection.Studios, LibraryIndexKind.Studio));
            Assert.Equal(170, LibraryCardMetrics.Height(LibraryView.Poster, 170, LibrarySection.Genres,
                LibraryIndexKind.Genre));
            Assert.Equal(169, LibraryCardMetrics.Height(LibraryView.Poster, 300, LibrarySection.Studios,
                LibraryIndexKind.Studio));

            // 形状不看「视图」：索引页没有「换成列表」这回事，两种卡片的宽高比只由这一页是哪种索引页决定。
            foreach (var view in Enum.GetValues<LibraryView>())
            {
                Assert.Equal(100, LibraryCardMetrics.Height(view, 100, LibrarySection.Genres, LibraryIndexKind.Genre));
                Assert.Equal(56, LibraryCardMetrics.Height(view, 100, LibrarySection.Studios, LibraryIndexKind.Studio));
            }

            // 八档图像大小逐档变宽，索引页跟着走（从前这两页完全不认这个设置）。
            var genreWidths = Enum.GetValues<LibraryCardSize>()
                .Select(size => LibraryCardMetrics.Width(LibraryView.Poster, size, LibrarySection.Genres, LibraryIndexKind.Genre))
                .ToArray();
            var studioWidths = Enum.GetValues<LibraryCardSize>()
                .Select(size => LibraryCardMetrics.Width(LibraryView.Poster, size, LibrarySection.Studios, LibraryIndexKind.Studio))
                .ToArray();
            for (var index = 1; index < genreWidths.Length; index++)
            {
                Assert.True(index == (int)LibraryCardSize.Default
                    ? genreWidths[index] == genreWidths[index - 1]
                    : genreWidths[index] > genreWidths[index - 1],
                    $"类型卡第 {index} 档没有比上一档宽");
                Assert.True(studioWidths[index] >= genreWidths[index], $"播出平台第 {index} 档比类型还窄");
            }
        });
        Test("索引页：类型要 Primary、播出平台先要 Thumb，台标留边不裁", () =>
        {
            Assert.Equal("Primary", string.Join(',', LibraryIndexCards.ImageTypes(LibraryIndexKind.Genre)));
            Assert.Equal("Thumb,Primary", string.Join(',', LibraryIndexCards.ImageTypes(LibraryIndexKind.Studio)));
            Assert.Equal(0, LibraryIndexCards.ImageTypes(LibraryIndexKind.None).Count);

            // 台标是画在透明底上的一块字形，铺满会把字切掉；四格封面本来就是画满的图，铺满才不留边。
            Assert.True(LibraryIndexCards.ContainArtwork(LibraryIndexKind.Studio));
            Assert.False(LibraryIndexCards.ContainArtwork(LibraryIndexKind.Genre));
            Assert.False(LibraryIndexCards.ContainArtwork(LibraryIndexKind.None));

            // 网页端 imagehelper.getShapeFromAspect 的分档：1.2 以下算方，1.4 以上算宽。
            Assert.Equal(1, LibraryIndexCards.Aspect(LibraryIndexKind.Genre));
            Assert.True(Math.Abs(LibraryIndexCards.Aspect(LibraryIndexKind.Studio) - 9.0 / 16.0) < 0.0001);
        });
        Test("索引页：设置面板只剩「标题」一项，也没有「分组到合集」", () =>
        {
            Assert.Equal(1, LibraryDisplayCatalog.FieldsFor(LibrarySection.Genres, LibraryView.Poster,
                LibraryIndexKind.Genre).Count);
            Assert.Equal("标题", LibraryDisplayCatalog.LabelOf(LibraryField.Title));
            Assert.Equal(1, LibraryDisplayCatalog.FieldsFor(LibrarySection.Studios, LibraryView.Poster,
                LibraryIndexKind.Studio).Count);

            // 下钻之后那一页装的是剧集，字段表要退回十四项。
            Assert.Equal(14, LibraryDisplayCatalog.FieldsFor(LibrarySection.Genres).Count);

            Assert.True(LibraryDisplayCatalog.Applies(LibraryField.Title, LibrarySection.Genres, LibraryView.Poster,
                LibraryIndexKind.Genre));
            Assert.False(LibraryDisplayCatalog.Applies(LibraryField.ProductionYear, LibrarySection.Genres,
                LibraryView.Poster, LibraryIndexKind.Genre));
            Assert.False(LibraryDisplayCatalog.Applies(LibraryField.Genres, LibrarySection.Genres,
                LibraryView.Poster, LibraryIndexKind.Genre));

            // 存下来的那一份是整库共用的：在索引页上只生效「标题」，别的项留着但不画。
            LibraryField[] saved = [LibraryField.Title, LibraryField.ProductionYear, LibraryField.Overview];
            Assert.Equal("Title", string.Join(',',
                LibraryDisplayCatalog.Applied(saved, LibrarySection.Genres, LibraryView.Poster, LibraryIndexKind.Genre)));
            Assert.Equal("Title,ProductionYear,Overview", string.Join(',',
                LibraryDisplayCatalog.Applied(saved, LibrarySection.Items)));

            var defaults = LibraryDisplayCatalog.DefaultFor(LibrarySection.Genres, LibraryIndexKind.Genre);
            Assert.Equal("Title", string.Join(',', defaults.Fields));
            Assert.False(defaults.GroupIntoCollections);
            // 非索引页的默认值不变：标题 + 年份，分组开着。
            var items = LibraryDisplayCatalog.DefaultFor(LibrarySection.Items);
            Assert.Equal("Title,ProductionYear", string.Join(',', items.Fields));
            Assert.True(items.GroupIntoCollections);
        });
        Test("索引页：请求只问标题那一项（外加宽高比），而且不发内容筛选", () =>
        {
            var display = new LibraryDisplay
            {
                Fields = [LibraryField.Title, LibraryField.Director, LibraryField.Overview],
                GroupIntoCollections = true
            };
            var genres = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Genres, 0, 20,
                EmbySortBy.Name, false, new(), null, display);

            // 勾了导演与概要也不该在类型页上问 People / Overview —— 索引页的字段表只有标题。
            var parameters = string.Join('&', genres.ToParameters().Select(pair => $"{pair.Key}={pair.Value}"));
            Assert.DoesNotContain("People", parameters);
            Assert.DoesNotContain("Overview", parameters);
            Assert.Contains("PrimaryImageAspectRatio", parameters);

            // 索引页恒按名称升序、不带内容筛选（筛选键在那一页本来就收起了）。
            Assert.Equal(EmbySortBy.Name, genres.SortBy);
            Assert.False(genres.Descending);
            Assert.True(genres.Filters is null || genres.Filters.IsBlank);

            // 下钻之后那一页才把筛选与用户挑的排序带上。
            var drilled = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Genres, 0, 20,
                EmbySortBy.DateAdded, true, new(), "动画", display);
            Assert.Equal("动画", drilled.Genre);
            Assert.Equal(EmbySortBy.DateAdded, drilled.SortBy);
            Assert.True(drilled.Descending);
        });
        Test("索引页：两种端点各自的路由与参数", () =>
        {
            // 索引页走顶层 /Genres 与 /Studios（不是 Users/{id}/Items），下钻也一样。
            Assert.Equal("Genres", LibraryBrowse.Path(LibrarySection.Genres, null, "user-1"));
            Assert.Equal("Studios", LibraryBrowse.Path(LibrarySection.Studios, null, "user-1"));
            Assert.Equal("Users/user-1/Items", LibraryBrowse.Path(LibrarySection.Genres, "动画", "user-1"));
            Assert.Equal("Users/user-1/Items", LibraryBrowse.Path(LibrarySection.Studios, "AT-X", "user-1"));

            // 下钻是把名字放进取值参数，不是塞进路径。比的是参数表而不是查询串 ——
            // ToQueryString 会把中文转义成 %E5%8A%A8%E7%94%BB。
            var genre = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Genres, 0, 20,
                EmbySortBy.Name, false, new(), "动画");
            Assert.Equal(("Genres", "动画"), genre.ToParameters().Single(pair => pair.Key == "Genres"));
            var studio = LibraryBrowse.Query("tvshows", "library-a", LibrarySection.Studios, 0, 20,
                EmbySortBy.Name, false, new(), "AT-X");
            Assert.Equal(("Studios", "AT-X"), studio.ToParameters().Single(pair => pair.Key == "Studios"));
        });
        Test("索引页：筛除/多选互不影响，分类顺序里两页都在", () =>
        {
            var sections = LibraryBrowse.Sections("tvshows", MediaServerKind.Emby);
            Assert.Equal("节目,推荐,预告,收藏,类型,播出平台,集,文件夹",
                string.Join(',', sections.Select(entry => entry.Label)));
            Assert.Equal("类型", sections.Single(entry => entry.Section == LibrarySection.Genres).Label);
            Assert.Equal("播出平台", sections.Single(entry => entry.Section == LibrarySection.Studios).Label);

            // Jellyfin 的标签另有一套，但类型与制片发行商两页仍在（它认这一套浏览条件）。
            var jellyfin = LibraryBrowse.Sections("tvshows", MediaServerKind.Jellyfin);
            Assert.True(jellyfin.Any(entry => entry.Section == LibrarySection.Genres));
            Assert.True(jellyfin.Any(entry => entry.Section == LibrarySection.Studios));

            // 飞牛与 Plex 装不出这一套条件，索引页整项不出现。
            foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
                Assert.False(LibraryBrowse.Sections("tvshows", kind).Any(entry =>
                    entry.Section is LibrarySection.Genres or LibrarySection.Studios), kind.ToString());
        });
        Test("索引页：类型与播出平台没有已观看/收藏可言", () =>
        {
            // 一张类型卡、一家电视台都不是能看的条目；服务器那边挂的是一份空的 UserData。
            Assert.False(EmbyItemType.TracksUserState(EmbyItemType.Genre));
            Assert.False(EmbyItemType.TracksUserState(EmbyItemType.Studio));
            Assert.True(EmbyItemType.TracksUserState(EmbyItemType.Series));
            Assert.True(EmbyItemType.TracksUserState(EmbyItemType.Movie));
            Assert.False(EmbyItemType.IsPlayable(EmbyItemType.Genre));
            Assert.False(EmbyItemType.IsPlayable(EmbyItemType.Studio));

            // 条目类型的中文名也有了（详情页那行、日志、菜单都用它）。
            Assert.Equal("类型", EmbyItemType.ToChinese(EmbyItemType.Genre));
            Assert.Equal("播出平台", EmbyItemType.ToChinese(EmbyItemType.Studio));
        });
        Test("索引页：一张卡的副标题不会被条目类型写出来", () =>
        {
            // 卡片上只写名字。EmbyItem.CardSubtitle 对没有年份的类型会把条目类型拼进去 —— 索引页上那会是
            // 「动画 / 类型」「AT-X / 播出平台」这种把同一个词说两遍的第二行，所以索引卡显式传空副标题
            // （见 LibraryViewModel.NewCard）。
            var genre = new EmbyItem { Id = "38", Name = "动画", Type = EmbyItemType.Genre };
            Assert.Equal("类型", genre.CardSubtitle);

            var studio = new EmbyItem { Id = "10740", Name = "Adult Swim", Type = EmbyItemType.Studio };
            Assert.Equal("播出平台", studio.CardSubtitle);

            // 所以索引页上真的取到的是那个名字本身，第二行必须由调用方压成空。
            Assert.Equal("动画", genre.Name);
            Assert.Equal("Adult Swim", studio.Name);
        });
        Test("索引页：底下只留一行字的高度，比普通卡片矮一截", () =>
        {
            // 普通卡底下是「名字 + 字段行」两行（CardSize.Chrome = 59），索引卡只有一个居中的名字，
            // 照 59 排每张卡底下会空出三十多像素，一行一行读下来就是「每隔一张空一行」。
            Assert.True(LibraryIndexCards.TextHeight < CardSize.Chrome);
            Assert.Equal(28, LibraryIndexCards.TextHeight);
        });
    }
}
