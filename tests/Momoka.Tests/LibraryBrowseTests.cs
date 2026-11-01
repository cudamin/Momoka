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
        Test("媒体库分类：电影、电视剧与混合内容按网页顺序分别显示", () =>
        {
            // 两张网页截图：电影库没有「合集」，混合内容库只有电影和节目／类型／文件夹。
            Assert.Equal("影片,推荐,预告片,类型,收藏,文件夹", Labels("movies", MediaServerKind.Emby));
            Assert.Equal("节目,推荐,预告,收藏,类型,播出平台,集,文件夹", Labels("tvshows", MediaServerKind.Emby));
            Assert.Equal("电影和节目,类型,文件夹", Labels("mixed", MediaServerKind.Emby));
            Assert.Equal(0, LibraryBrowse.Sections("music", MediaServerKind.Emby).Count);
        });
        Test("混合内容识别：没有 CollectionType 且 Subviews 同时含电影与剧集", () =>
        {
            // 网页端 dashboard-ui/videos/videos.js 的 HomeVideosView.getTabs 就是这个判据；
            // Emby 建混合内容库时把 mixed 写成 null（medialibrarycreator.js）。
            Assert.True(LibraryBrowse.IsMixedContent(null, ["movies", "series", "genres", "folders"]));
            Assert.False(LibraryBrowse.IsMixedContent(null, ["movies", "genres", "folders"]));
            Assert.False(LibraryBrowse.IsMixedContent(null, ["series", "genres", "folders"]));
            Assert.False(LibraryBrowse.IsMixedContent(null, null));
            Assert.False(LibraryBrowse.IsMixedContent("movies", ["movies", "series"]));
        });
        Test("媒体库分类：不支持的服务端入口隐藏", () =>
        {
            // Jellyfin Web 10.10 的标签是另一套：电影有合集、没有文件夹；节目没有收藏与文件夹。
            Assert.Equal("节目,建议,即将播出,类型,制片发行商,剧集", Labels("tvshows", MediaServerKind.Jellyfin));
            Assert.Equal("电影,建议,预告片,我的最爱,合集,类型", Labels("movies", MediaServerKind.Jellyfin));
            // Jellyfin 的混合内容库没有分区签：API 里 CollectionType 是 null，网页端走 list.html。
            // 即便某个版本报 "mixed"，JellyfinLibrary 也不为它编出三栏。
            Assert.Equal(0, LibraryBrowse.Sections("mixed", MediaServerKind.Jellyfin).Count);
            Assert.Equal("影片", Labels("movies", MediaServerKind.FnNas));
            Assert.Equal("节目", Labels("tvshows", MediaServerKind.FnNas));
            Assert.Equal("电影和节目", Labels("mixed", MediaServerKind.FnNas));
            Assert.Equal("节目", Labels("tvshows", MediaServerKind.Plex));
            Assert.Equal("电影和节目", Labels("mixed", MediaServerKind.Plex));
        });
        Test("媒体库分类：飞牛库页不摆分区签，分区表只剩内容那一项", () =>
        {
            // 飞牛的库页整体收起分区签，库根进来时分区恒为 Items —— 分区表再留「收藏」就是一份
            // 没有出口的配置（收藏仍在标题栏收藏页、详情页与卡片菜单可达）。
            foreach (var type in new[] { "movies", "tvshows", "mixed" })
            {
                var sections = LibraryBrowse.Sections(type, MediaServerKind.FnNas);
                Assert.Equal(1, sections.Count, type);
                Assert.Equal(LibrarySection.Items, sections[0].Section, type);
                Assert.False(sections.Any(entry => entry.Section == LibrarySection.Favorites), type);
            }
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
        Test("媒体库分类：单集、预告、文件夹与三种库的内容类型分别查询", () =>
        {
            Assert.Equal("Episode", string.Join(',', Query(LibrarySection.Episodes).IncludeItemTypes));
            Assert.Equal("Episode", string.Join(',', Query(LibrarySection.Upcoming).IncludeItemTypes));
            Assert.Equal("Trailer", string.Join(',', Query(LibrarySection.Trailers).IncludeItemTypes));
            Assert.Equal(0, Query(LibrarySection.Folders).IncludeItemTypes.Count);
            Assert.Equal("Movie,Video", string.Join(',', Query(LibrarySection.Items, collectionType: "movies").IncludeItemTypes));
            Assert.Equal("Series,Video", string.Join(',', Query(LibrarySection.Items, collectionType: "tvshows").IncludeItemTypes));
            Assert.Equal("Movie,Series,Video", string.Join(',', Query(LibrarySection.Items, collectionType: "mixed").IncludeItemTypes));
            Assert.False(LibraryBrowse.Query("movies", "library-a", LibrarySection.Items, 0, 20,
                EmbySortBy.Name, false, new()).IncludeItemTypes.Contains(EmbyItemType.BoxSet));
        });
        Test("媒体库分类：收藏为不可清除的固定条件", () =>
        {
            var filters = new ItemFilters();
            Assert.True(Query(LibrarySection.Favorites, filters: filters).IsFavorite);
            Assert.Contains("Filters=IsFavorite", Query(LibrarySection.Favorites, filters: filters).ToQueryString());
        });
        Test("媒体库分类：Jellyfin 合集页只问合集本身", () =>
        {
            var query = Query(LibrarySection.Collections);
            Assert.Equal("BoxSet", string.Join(',', query.IncludeItemTypes));
            Assert.True(query.Recursive);
            Assert.Equal("library-a", query.ParentId);
            Assert.False(query.IsFavorite);
            Assert.Null(query.GroupItemsIntoCollections);
            Assert.True(query.Filters!.IsBlank);
        });
        Test("Jellyfin 库：标签、排序菜单与默认排序照网页端", () =>
        {
            Assert.Equal(
                "名称,随机,公众评分,影评人评分,加入日期,播放日期,家长分级,播放次数,发行日期,播放时长",
                JellyfinSortLabels("movies", LibrarySection.Items));
            Assert.Equal("名称,随机,公众评分,节目添加日期,剧集添加日期,播放日期,家长分级,发行日期",
                JellyfinSortLabels("tvshows", LibrarySection.Items));
            Assert.Equal("名称,TVDB 评分,加入日期,首映日期,播放日期,家长分级,播放次数,播放时长",
                JellyfinSortLabels("tvshows", LibrarySection.Episodes));
            Assert.Equal("名称,公众评分,加入日期,播放日期,家长分级,播放次数,发行日期",
                JellyfinSortLabels("movies", LibrarySection.Trailers));
            Assert.Equal("名称,公众评分,加入日期,家长分级,发行日期",
                JellyfinSortLabels("movies", LibrarySection.Collections));
            // list.js 的 getSortMenuOptions 把「文件夹」推在播放日期之后，不在第二位；混合内容库
            // 与文件夹下钻页都是这一份菜单。
            Assert.Equal("名称,公众评分,影评人评分,加入日期,播放日期,文件夹,家长分级,播放次数,发行日期,播放时长",
                JellyfinSortLabels("mixed", LibrarySection.Items));
            Assert.Equal("名称,公众评分,影评人评分,加入日期,播放日期,文件夹,家长分级,播放次数,发行日期,播放时长",
                JellyfinSortLabels(null, LibrarySection.Items));

            Assert.Equal("SortName,ProductionYear", JellyfinLibrary.DefaultSort("movies", LibrarySection.Items).Value);
            Assert.Equal("SortName", JellyfinLibrary.DefaultSort("tvshows", LibrarySection.Items).Value);
            Assert.Equal("SeriesSortName,SortName", JellyfinLibrary.DefaultSort("tvshows", LibrarySection.Episodes).Value);
            Assert.Equal("SortName", JellyfinLibrary.DefaultSort("movies", LibrarySection.Collections).Value);
            Assert.Equal("IsFolder,SortName", JellyfinLibrary.DefaultSort("mixed", LibrarySection.Items).Value);
            Assert.True(JellyfinLibrary.IsNameSort("SortName,ProductionYear"));
            Assert.True(JellyfinLibrary.IsNameSort("IsFolder,SortName"));
            Assert.False(JellyfinLibrary.IsNameSort("DateCreated,SortName"));
        });
        Test("Jellyfin 库：筛选面板按网页端的复选框组", () =>
        {
            Assert.Equal("播放状态,功能,类型,家长分级,标签,视频类型,年份",
                JellyfinFilterLabels("movies", LibrarySection.Items));
            Assert.Equal("播放状态,状态,功能,类型,家长分级,标签,年份",
                JellyfinFilterLabels("tvshows", LibrarySection.Items));
            Assert.Equal("播放状态,剧集,功能,类型,家长分级,标签,视频类型,年份",
                JellyfinFilterLabels("tvshows", LibrarySection.Episodes));
            Assert.Equal("播放状态,功能,类型,家长分级,标签,视频类型,年份",
                JellyfinFilterLabels("movies", LibrarySection.Trailers));
            Assert.Equal(0, JellyfinFilterLabels("movies", LibrarySection.Collections).Length);
            Assert.Equal(0, JellyfinFilterLabels("tvshows", LibrarySection.Genres).Length);

            Assert.Equal("已播放,未播放,可恢复播放,我的最爱",
                JellyfinCheckLabels("movies", LibrarySection.Items, "jellyfinStatus"));
            Assert.Equal("连载中,完结,暂未发布",
                JellyfinCheckLabels("tvshows", LibrarySection.Items, "jellyfinSeriesStatus"));
            Assert.Equal("特集,缺少的剧集,未播出的剧集",
                JellyfinCheckLabels("tvshows", LibrarySection.Episodes, "jellyfinEpisodes"));
            Assert.Equal("字幕,预告片,花絮,主题曲,主题视频",
                JellyfinCheckLabels("movies", LibrarySection.Items, "jellyfinFeatures"));
            Assert.Equal("蓝光,DVD,HD高清,4K,SD标清,三维",
                JellyfinCheckLabels("movies", LibrarySection.Items, "jellyfinVideoTypes"));

            // list.html 的筛选是 filtermenu.js：四颗状态复选、服务器类型、六种视频类型、功能；
            // 没有家长分级／标签／年份。
            Assert.Equal("播放状态,类型,视频类型,功能",
                JellyfinFilterLabels(null, LibrarySection.Items));
            Assert.Equal("播放状态,类型,视频类型,功能",
                JellyfinFilterLabels("mixed", LibrarySection.Items));
            Assert.Equal("未播放,已播放,我的最爱,继续观看",
                JellyfinCheckLabels(null, LibrarySection.Items, "jellyfinListStatus"));
            Assert.Equal("HD,4K,SD,3D,Blu-ray,DVD",
                JellyfinCheckLabels(null, LibrarySection.Items, "jellyfinListVideoTypes"));
            Assert.Equal("字幕,预告片,额外,主题曲,主题视频",
                JellyfinCheckLabels(null, LibrarySection.Items, "jellyfinListFeatures"));
            Assert.True(JellyfinLibrary.HasFilter(null, LibrarySection.Items));
        });
        Test("Jellyfin 库：筛选选项进入查询参数，列表分隔符照服务器", () =>
        {
            var filters = new ItemFilters
            {
                Toggles = ["played", "is4k", "bluray", "specialEpisode", "missingEpisode", "futureEpisode", "unreleased"],
                Genres = ["动画"],
                OfficialRatings = ["PG-13"],
                Tags = ["科幻"],
                Years = ["2024"]
            };
            var text = new ItemQuery { Filters = filters, Limit = 20 }.ToQueryString();
            Assert.Contains("Filters=IsPlayed", text);
            Assert.Contains("Is4K=true", text);
            Assert.Contains("VideoTypes=Bluray", text);
            Assert.Contains("ParentIndexNumber=0", text);
            Assert.Contains("IsMissing=true", text);
            Assert.Contains("IsUnaired=true", text);
            Assert.Contains("SeriesStatus=Unreleased", text);
            Assert.Contains("Genres=%E5%8A%A8%E7%94%BB", text);
            Assert.Contains("OfficialRatings=PG-13", text);
            Assert.Contains("Tags=%E7%A7%91%E5%B9%BB", text);
            Assert.Contains("Years=2024", text);
        });
        Test("Jellyfin 库：恢复筛选时只留这套面板认得的选项", () =>
        {
            var filters = new ItemFilters
            {
                Toggles = ["played", "duplicate", "locked", "favorite", "subtitles"],
                Genres = ["动画"],
                MediaValues = { ["Containers"] = ["mkv"] }
            };
            var pruned = JellyfinLibrary.Prune(filters);
            Assert.Equal("played,favorite,subtitles", string.Join(',', pruned.Toggles));
            Assert.Equal("动画", string.Join(',', pruned.Genres));
            Assert.Equal(0, pruned.MediaValues.Count);
            Assert.Equal(4, pruned.Count);
        });
        Test("Jellyfin 库：布局菜单与恢复时的视图裁剪", () =>
        {
            Assert.Equal("Banner,List,Poster,PosterCard,Thumb,ThumbCard",
                ViewLabels("movies", LibrarySection.Items));
            Assert.Equal("List,Poster,PosterCard,Thumb,ThumbCard",
                ViewLabels("movies", LibrarySection.Collections));
            Assert.Equal("List,Poster,PosterCard", ViewLabels("tvshows", LibrarySection.Episodes));
            Assert.Equal("Poster", ViewLabels("movies", LibrarySection.Trailers));
            Assert.Equal("Poster", ViewLabels("tvshows", LibrarySection.Genres));
            // 混合内容库与文件夹（CollectionType 为 null）用 viewSettings.template.html 的六项：
            // 封面图、横幅图、光盘封面图、徽标、缩略图、列表。
            Assert.Equal("Poster,Banner,Disc,Logo,Thumb,List", ViewLabels(null, LibrarySection.Items));
            Assert.Equal("Poster,Banner,Disc,Logo,Thumb,List", ViewLabels("mixed", LibrarySection.Items));
            Assert.Equal("封面图,横幅图,光盘封面图,徽标,缩略图,列表",
                string.Join(',', JellyfinLibrary.Views(null, LibrarySection.Items).Select(JellyfinLibrary.ViewLabel)));
            Assert.True(JellyfinLibrary.HasLayout("movies", LibrarySection.Items));
            Assert.True(JellyfinLibrary.HasLayout("movies", LibrarySection.Collections));
            Assert.True(JellyfinLibrary.HasLayout("tvshows", LibrarySection.Episodes));
            Assert.True(JellyfinLibrary.HasLayout(null, LibrarySection.Items));
            Assert.False(JellyfinLibrary.HasLayout("movies", LibrarySection.Trailers));
            Assert.False(JellyfinLibrary.HasLayout("tvshows", LibrarySection.Genres));
            Assert.True(JellyfinLibrary.HasAlphabetPicker(null, LibrarySection.Items));

            var restored = JellyfinLibrary.Fit(new LibraryDisplay
            {
                View = LibraryView.Table,
                GroupIntoCollections = true,
                Fields = [LibraryField.Title]
            }, "movies", LibrarySection.Items);
            Assert.Equal(LibraryView.Poster, restored.View);
            Assert.False(restored.GroupIntoCollections);
            Assert.Equal("Title", string.Join(',', restored.Fields));

            var card = JellyfinLibrary.Fit(new LibraryDisplay { View = LibraryView.PosterCard },
                "movies", LibrarySection.Items);
            Assert.Equal(LibraryView.PosterCard, card.View);

            var list = JellyfinLibrary.Fit(new LibraryDisplay { View = LibraryView.Logo },
                null, LibrarySection.Items);
            Assert.Equal(LibraryView.Logo, list.View);
            var fallback = JellyfinLibrary.Fit(new LibraryDisplay { View = LibraryView.Table },
                null, LibrarySection.Items);
            Assert.Equal(LibraryView.Poster, fallback.View);
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
        Test("媒体库分类：排序与筛选用的是两份预设", () =>
        {
            Assert.Equal(EmbySortPreset.Episode, LibraryBrowse.SortPreset("tvshows", LibrarySection.Episodes));
            // 电影库的预告片排序菜单与影片、收藏是同一份；筛选用的是 Trailer 那份。
            Assert.Equal(EmbySortPreset.Movie, LibraryBrowse.SortPreset("movies", LibrarySection.Trailers));
            Assert.Equal(EmbySortPreset.Trailer, LibraryBrowse.FilterPreset("movies", LibrarySection.Trailers));
            Assert.Equal(EmbySortPreset.Movie, LibraryBrowse.FilterPreset("movies", LibrarySection.Items));
            Assert.Equal(EmbySortPreset.Movie, LibraryBrowse.FilterPreset("movies", LibrarySection.Favorites));
            Assert.Equal(EmbySortPreset.Series, LibraryBrowse.FilterPreset("tvshows", LibrarySection.Items));
            Assert.Equal(EmbySortPreset.Episode, LibraryBrowse.FilterPreset("tvshows", LibrarySection.Episodes));
            Assert.Equal(EmbySortPreset.Mixed, LibraryBrowse.FilterPreset("mixed", LibrarySection.Items));
            Assert.Equal<string?>(null, LibraryBrowse.FilterPreset("mixed", LibrarySection.Folders));
            Assert.Equal<string?>(null, LibraryBrowse.SortPreset("movies", LibrarySection.Folders));
            Assert.Equal<string?>(null, LibraryBrowse.SortPreset("mixed", LibrarySection.Items));
            Assert.Equal<string?>(null, LibraryBrowse.SortPreset("mixed", LibrarySection.Folders));
        });
        Test("媒体库分类：电影排序菜单十九条、混合内容用文件夹那份二十五条", () =>
        {
            const string movie =
                "IMDb 评分,分辨率,加入日期,发行日期,媒体容器,家长评分,导演,帧率,年份,影评人评分,播放日期,播放时长,播放次数,文件名,文件尺寸,标题,比特率,视频编解码器,随机";
            Assert.Equal(movie, SortLabels("movies", LibrarySection.Items));
            Assert.Equal(movie, SortLabels("movies", LibrarySection.Trailers));
            Assert.Equal(movie, SortLabels("movies", LibrarySection.Favorites));
            // 类型下钻之后装的是这一库的条目，菜单跟着这一库走；索引页本身不显示排序。
            Assert.Equal(movie, SortLabels("movies", LibrarySection.Genres));
            Assert.Equal(
                "上次添加内容日期,专辑,专辑艺人,作曲家,分辨率,加入日期,发行日期,媒体容器,家长评分,导演,帧率,年份,影评人评分,播放日期,播放时长,播放次数,文件名,文件尺寸,标题,比特率,社区评分,编号,艺人,视频编解码器,随机",
                SortLabels("mixed", LibrarySection.Items));
            Assert.Equal(SortLabels("mixed", LibrarySection.Folders), SortLabels("mixed", LibrarySection.Items));
            Assert.Equal(SortLabels("mixed", LibrarySection.Folders), SortLabels("mixed", LibrarySection.Genres));
            Assert.Equal(
                "IMDb 评分,上次发布集日期,上次添加集日期,加入日期,发行日期,家长评分,导演,年份,影评人评分,播放日期,播放时长,标题,随机",
                SortLabels("tvshows", LibrarySection.Genres));
        });
        Test("媒体库分类：电影、预告片与混合内容的筛选行照截图", () =>
        {
            Assert.Equal(
                "播放状态,收藏,重制,类型,家长评分,工作室,年份,媒体容器,视频编解码器,视频类型,分辨率,3D,音频编解码器,音频布局,音频语言,字幕,字幕编解码器,字幕语言,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                FilterLabels(EmbySortPreset.Movie));
            Assert.Equal(
                "播放状态,收藏,分辨率,3D,字幕,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                FilterLabels(EmbySortPreset.Trailer));
            Assert.Equal(
                "播放状态,收藏,类型,家长评分,工作室,年份,媒体容器,分辨率,3D,字幕,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                FilterLabels(EmbySortPreset.Mixed));
        });
        Test("媒体库分类：电影默认加入日期降序，混合内容默认标题升序", () =>
        {
            foreach (var section in new[] { LibrarySection.Items, LibrarySection.Favorites })
            {
                var movie = LibrarySortCatalog.Default("movies", section);
                Assert.Equal(EmbySortBy.DateAdded, movie.Value);
                Assert.True(movie.DescendingByDefault);
            }
            var trailer = LibrarySortCatalog.Default("movies", LibrarySection.Trailers);
            Assert.Equal(EmbySortBy.Name, trailer.Value);
            Assert.False(trailer.DescendingByDefault);
            var mixed = LibrarySortCatalog.Default("mixed", LibrarySection.Items);
            Assert.Equal(EmbySortBy.Name, mixed.Value);
            Assert.False(mixed.DescendingByDefault);
            Assert.Equal(EmbySortBy.Name, LibrarySortCatalog.Default("tvshows", LibrarySection.Items).Value);
        });
        Test("媒体库分类：Emby 实际请求路径、作用域、正文与凭据边界",
            () => RequestsAsync("tvshows", MediaServerKind.Emby).GetAwaiter().GetResult());
        Test("媒体库分类：Jellyfin 新路由、类型和平台保持作用域",
            () => RequestsAsync("tvshows", MediaServerKind.Jellyfin).GetAwaiter().GetResult());
        Test("媒体库分类：混合内容库按电影和剧集查询且保持作用域",
            () => RequestsAsync("mixed", MediaServerKind.Emby).GetAwaiter().GetResult());
        Test("媒体库分类：Plex 不支持的入口不发网络请求", () =>
        {
            using var handler = new Transport();
            using var http = new EmbyHttp(handler);
            var client = Client(http, MediaServerKind.Plex);
            Assert.Throws<NotSupportedException>(() => client.GetLibraryItemsAsync("movies", LibrarySection.Genres,
                null, Query(LibrarySection.Genres), default));
            Assert.Equal(0, handler.Requests.Count);
        });
        Test("能力：跨库题材页只有 Emby 与 Jellyfin 有路，Plex 与飞牛都没有", () =>
        {
            // 详情页那一行类型点进去问的是「这个类型下的全部影片和剧集」：Plex 客户端没有这条路，
            // 飞牛的适配器对 Genre 条件直接抛 NotSupportedException（见 FnNasMediaTests 那一条）。
            // 这一句是详情页那一行画不画链接的唯一判据，所以四种来源都要钉住。
            using var handler = new Transport();
            using var http = new EmbyHttp(handler);
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
                Assert.True(Client(http, kind).Connection.SupportsGenreBrowse, kind + " 应该能按类型浏览");
            foreach (var kind in new[] { MediaServerKind.Plex, MediaServerKind.FnNas })
                Assert.False(Client(http, kind).Connection.SupportsGenreBrowse, kind + " 没有跨库题材页");
            Assert.Equal(0, handler.Requests.Count);
        });
        Test("能力：分组到合集只有 Emby 的列表接口认，Jellyfin、Plex 与飞牛都没有", () =>
        {
            // 设置面板据此收起「将项目分组到合集中」那一组：Jellyfin 的请求在协议层被删掉这个参数
            // （EmbyClient.Protocol.cs），Plex 与飞牛的适配器从不读它（PlexMediaAdapter.Models.cs 只翻
            // 排序、类型与播放状态）。这一句是那一组收不收的唯一判据，所以四种来源都要钉住。
            Assert.True(LibraryBrowse.SupportsCollectionGrouping(MediaServerKind.Emby), "Emby 的列表接口认这个参数");
            foreach (var kind in new[] { MediaServerKind.Jellyfin, MediaServerKind.Plex, MediaServerKind.FnNas })
                Assert.False(LibraryBrowse.SupportsCollectionGrouping(kind), kind + " 没有「分组到合集」");
        });
        Test("电影推荐：Emby 与 Jellyfin 都问 Movies/Recommendations，且带本库作用域与网页端上限", async () =>
        {
            // 上游 moviesuggestions.js 的 fetchSections 发的是 CategoryLimit=6、竖向 ItemLimit=12、ParentId 本库；
            // 这三项决定电影推荐页「有几个理由排、每排多少张、问的是哪个库」。
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                using var handler = new Transport();
                using var http = new EmbyHttp(handler);
                var rows = await Client(http, kind).GetMovieRecommendationsAsync(
                    "library-a", LibraryRecommended.EmbyMovieRecommendationSize, default);
                Assert.Equal(0, rows.Count);
                var uri = handler.Requests.Last();
                Assert.Equal("/media/Movies/Recommendations", uri.AbsolutePath);
                Assert.Contains("ParentId=library-a", uri.Query);
                Assert.Contains("CategoryLimit=6", uri.Query);
                Assert.Contains("ItemLimit=12", uri.Query);
            }
            using var plex = new Transport();
            using var plexHttp = new EmbyHttp(plex);
            var rejected = false;
            try { await Client(plexHttp, MediaServerKind.Plex).GetMovieRecommendationsAsync("library-a", 12, default); }
            catch (NotSupportedException) { rejected = true; }
            Assert.True(rejected, "Plex 不该有电影推荐接口");
            Assert.Equal(0, plex.Requests.Count);
        });
    }

    private static string Labels(string type, MediaServerKind kind) =>
        string.Join(',', LibraryBrowse.Sections(type, kind).Select(entry => entry.Label));
    private static string SortLabels(string collectionType, LibrarySection section) =>
        string.Join(',', LibrarySortCatalog.For(collectionType, section, new Version(4, 10, 1)).Select(option => option.Label));
    private static string JellyfinSortLabels(string? collectionType, LibrarySection section) =>
        string.Join(',', JellyfinLibrary.SortOptions(collectionType, section).Select(option => option.Label));
    private static string JellyfinFilterLabels(string? collectionType, LibrarySection section) =>
        string.Join(',', JellyfinLibrary.FilterFields(collectionType, section).Select(field => field.Label));
    private static string JellyfinCheckLabels(string? collectionType, LibrarySection section, string field) =>
        string.Join(',', JellyfinLibrary.FilterFields(collectionType, section)
            .Single(entry => entry.Id == field)
            .Choices
            .Select(choice => choice.Label));
    private static string ViewLabels(string? collectionType, LibrarySection section) =>
        string.Join(',', JellyfinLibrary.Views(collectionType, section)
            .Select(view => view.ToString()));
    private static string FilterLabels(string preset) =>
        string.Join(',', EmbyFilterBy.For(preset, new Version(4, 10, 1)).Select(field => field.Label));
    private static ItemQuery Query(LibrarySection section, string? group = null, ItemFilters? filters = null,
        string collectionType = "tvshows") =>
        LibraryBrowse.Query(collectionType, "library-a", section, 20, 20, EmbySortBy.DateAdded, true, filters ?? new(), group);
    private static EmbyClient Client(EmbyHttp http, MediaServerKind kind) => new(http,
        new EmbyConnection(new Uri("https://fixture.invalid/media/"), "fixture-token", "user", "User", "Fixture",
            DeviceIdentity.Create("library-fixture", "1"))
        { ServerKind = kind, ServerVersion = "12.2.0" });

    private static async Task RequestsAsync(string collectionType, MediaServerKind kind)
    {
        using var handler = new Transport();
        using var http = new EmbyHttp(handler);
        var client = Client(http, kind);
        foreach (var entry in LibraryBrowse.Sections(collectionType, kind))
        {
            // Jellyfin 的「建议」页由 Movies/Recommendations、Items/Latest、NextUp 三份货架数据拼成，
            // 不走这条普通列表接口；它的控制器在网页端也一样。
            if (kind == MediaServerKind.Jellyfin && entry.Section == LibrarySection.Recommended) continue;
            await client.GetLibraryItemsAsync(collectionType, entry.Section, null,
                Query(entry.Section, collectionType: collectionType), default);
            var uri = handler.Requests.Last();
            Assert.Contains("ParentId=library-a", uri.Query);
            Assert.DoesNotContain("fixture-token", uri.AbsoluteUri);
            if (collectionType == "mixed" && entry.Section == LibrarySection.Items)
                Assert.Contains("IncludeItemTypes=Movie%2CSeries%2CVideo", uri.Query);
            if (entry.Section == LibrarySection.Collections)
                Assert.Contains("IncludeItemTypes=BoxSet", uri.Query);
            var path = LibraryBrowse.Path(entry.Section, null, "user");
            if (kind == MediaServerKind.Jellyfin && path == "Users/user/Items") path = "Items";
            Assert.Equal("/media/" + path, uri.AbsolutePath);
        }
        await client.GetLibraryItemsAsync(collectionType, LibrarySection.Genres, "动画",
            Query(LibrarySection.Genres, "动画", collectionType: collectionType), default);
        Assert.Contains("Genres=%E5%8A%A8%E7%94%BB", handler.Requests.Last().Query);
        Assert.Contains("ParentId=library-a", handler.Requests.Last().Query);
        if (collectionType != "tvshows") return;
        await client.GetLibraryItemsAsync(collectionType, LibrarySection.Studios, "TBS & Tokyo",
            Query(LibrarySection.Studios, "TBS & Tokyo", collectionType: collectionType), default);
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
            var body = request.RequestUri!.AbsolutePath.EndsWith("Movies/Recommendations", StringComparison.Ordinal)
                ? "[]"
                : "{\"Items\":[],\"TotalRecordCount\":0}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
