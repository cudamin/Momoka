using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 推荐页那三排：哪几个库有、每一排问什么、卡片底下那两行字怎么写。
/// <para>
/// 判据对着 2026-10-17 用户给的网页端截图定：继续观看（横卡、SxxExx - 集名）、最新集（竖版海报、年份区间、
/// 右上角未看集数）、最近发布的剧集（横卡、SxxExx - 集名）。这里钉的是「说什么」，屏幕上的排版由探针看。
/// </para>
/// </summary>
internal static class LibraryRecommendedTests
{
    public static void Register()
    {
        Test("推荐页：Emby 的电视剧三排、电影两排", () =>
        {
            var shows = LibraryRecommended.Rows("tvshows");
            Assert.Equal(3, shows.Count);
            Assert.Equal("继续观看,最新集,最近发布的剧集", string.Join(',', shows.Select(row => row.Title)));
            // 继续观看与最近发布的剧集是 16:9 横卡，最新集是 2:3 海报。
            Assert.Equal("True,False,True", string.Join(',', shows.Select(row => row.Wide)));

            // 网页端电影库（videos/moviesuggestions.js）的两排固定货架：继续观看是横卡（preferThumb），
            // 最新影片是竖版海报。服务器那几排推荐接在它们后面，标题由 RecommendationType 合成。
            var movies = LibraryRecommended.Rows("movies");
            Assert.Equal(2, movies.Count);
            Assert.Equal("继续观看,最新影片", string.Join(',', movies.Select(row => row.Title)));
            Assert.Equal("True,False", string.Join(',', movies.Select(row => row.Wide)));

            Assert.Equal(0, LibraryRecommended.Rows(null).Count);
            Assert.Equal(0, LibraryRecommended.Rows("music").Count);
            // Plex 与飞牛的电影库没有这组接口，推荐那一格维持原样（分区签在别处按服务器收起）。
            Assert.Equal(0, LibraryRecommended.Rows("movies", MediaServerKind.Plex).Count);
            Assert.Equal(0, LibraryRecommended.Rows("movies", MediaServerKind.FnNas).Count);
        });

        Test("推荐页：服务器推荐的标题照网页端那一句", () =>
        {
            // 四句标题取自上游 moviesuggestions.js 的 RecommendationType 分支，译文照真实 Emby 服务器
            // 网页语言包：BecauseYouWatchedValue / BecauseYouLikeValue / DirectedByValue / StarringValue。
            Assert.Equal("类似看过：离线媒体库内容", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "SimilarToRecentlyPlayed", BaselineItemName = "离线媒体库内容" }));
            Assert.Equal("类似收藏：离线媒体库内容", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "SimilarToLikedItem", BaselineItemName = "离线媒体库内容" }));
            Assert.Equal("同一执导：离线媒体库内容", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "HasDirectorFromRecentlyPlayed", BaselineItemName = "离线媒体库内容" }));
            Assert.Equal("同一执导：离线媒体库内容", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "HasLikedDirector", BaselineItemName = "离线媒体库内容" }));
            Assert.Equal("同一出演：离线媒体库内容", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "HasActorFromRecentlyPlayed", BaselineItemName = "离线媒体库内容" }));
            Assert.Equal("同一出演：离线媒体库内容", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "HasLikedActor", BaselineItemName = "离线媒体库内容" }));

            // 没有基准片名时退到 Jellyfin 电影那一排用的那一句，不留一个「同一执导：」的空尾巴。
            Assert.Equal("为你推荐", LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "SimilarToRecentlyPlayed", BaselineItemName = "  " }));
            // 认不出来的类型不上屏（网页端那一下会把标题留成空串，摆出来是一排没有牌子的卡）。
            Assert.Null(LibraryRecommended.RecommendationTitle(
                new MovieRecommendation { RecommendationType = "SomethingElse", BaselineItemName = "离线媒体库内容" }));
            Assert.Null(LibraryRecommended.RecommendationTitle(new MovieRecommendation { BaselineItemName = "离线媒体库内容" }));
        });

        Test("推荐页：服务器推荐按类型成排，同名合并、条目去重", () =>
        {
            // 空条目与未知类型的分类各跳过一条：没有卡片的排不占屏幕，认不出的类型没有牌子可挂。
            var rows = LibraryRecommended.RecommendationRows(
            [
                new MovieRecommendation
                {
                    RecommendationType = "SimilarToRecentlyPlayed",
                    BaselineItemName = "离线媒体库内容",
                    Items =
                    [
                        new EmbyItem { Id = "a", Name = "甲", Type = EmbyItemType.Movie },
                        new EmbyItem { Id = "b", Name = "乙", Type = EmbyItemType.Movie }
                    ]
                },
                new MovieRecommendation
                {
                    RecommendationType = "HasLikedDirector",
                    BaselineItemName = "离线媒体库内容",
                    Items = []
                },
                new MovieRecommendation
                {
                    RecommendationType = "Unknown",
                    BaselineItemName = "离线媒体库内容",
                    Items = [new EmbyItem { Id = "c", Name = "丙", Type = EmbyItemType.Movie }]
                },
                new MovieRecommendation
                {
                    RecommendationType = "HasActorFromRecentlyPlayed",
                    BaselineItemName = "离线媒体库内容",
                    Items = [new EmbyItem { Id = "a", Name = "甲", Type = EmbyItemType.Movie }]
                },
                // 同一句标题（同一部基准片）出现两次时并成一排，重复的片子只留一次。
                new MovieRecommendation
                {
                    RecommendationType = "SimilarToRecentlyPlayed",
                    BaselineItemName = "离线媒体库内容",
                    Items =
                    [
                        new EmbyItem { Id = "b", Name = "乙", Type = EmbyItemType.Movie },
                        new EmbyItem { Id = "d", Name = "丁", Type = EmbyItemType.Movie },
                        new EmbyItem { Id = "e", Name = "戊", Type = EmbyItemType.Audio }
                    ]
                }
            ]);

            // 顺序跟服务器给的分类次序走，不按字典序重排。
            Assert.Equal(2, rows.Count);
            Assert.Equal("类似看过：离线媒体库内容,同一出演：离线媒体库内容",
                string.Join(',', rows.Select(row => row.Row.Title)));
            Assert.Equal("False,False", string.Join(',', rows.Select(row => row.Row.Wide)));
            Assert.Equal(RecommendedShelf.MovieRecommendations, rows[0].Row.Shelf);
            // 第一条分类的两部加第二条分类带过来的「丁」，音乐类型与重复的「乙」都不进这一排。
            Assert.Equal("a,b,d", string.Join(',', rows[0].Items.Select(item => item.Id)));
            Assert.Equal("a", string.Join(',', rows[1].Items.Select(item => item.Id)));
        });

        Test("推荐页：Jellyfin 的电影与剧集各有一套货架", () =>
        {
            var movies = LibraryRecommended.Rows("movies", MediaServerKind.Jellyfin);
            Assert.Equal("继续观看,最新电影,为你推荐", string.Join(',', movies.Select(row => row.Title)));
            Assert.Equal("True,False,False", string.Join(',', movies.Select(row => row.Wide)));

            var shows = LibraryRecommended.Rows("tvshows", MediaServerKind.Jellyfin);
            Assert.Equal("继续观看,最新剧集,接下来看", string.Join(',', shows.Select(row => row.Title)));
            Assert.Equal("True,True,True", string.Join(',', shows.Select(row => row.Wide)));

            Assert.Equal("Movie", string.Join(',',
                LibraryRecommended.Query("library-a", RecommendedShelf.LatestMovies).IncludeItemTypes));
            Assert.Equal("DateCreated,SortName",
                LibraryRecommended.Query("library-a", RecommendedShelf.LatestMovies).SortBy);
            Assert.Equal("", LibraryRecommended.Query("library-a", RecommendedShelf.NextUp).SortBy);
            Assert.Equal("", LibraryRecommended.Query("library-a", RecommendedShelf.MovieRecommendations).SortBy);
        });

        Test("推荐页：最新集问的是剧集本身，按最近入库排", () =>
        {
            var query = LibraryRecommended.Query("library-a", RecommendedShelf.LatestEpisodes);
            Assert.Equal("library-a", query.ParentId);
            Assert.True(query.Recursive);
            Assert.Equal("Series", string.Join(',', query.IncludeItemTypes));
            Assert.Equal("DateLastContentAdded,SortName", query.SortBy);
            Assert.True(query.Descending);
            Assert.Equal(LibraryRecommended.Size, query.Limit);
            var text = query.ToQueryString();
            Assert.Contains("ParentId=library-a", text);
            Assert.Contains("SortOrder=Descending", text);
        });

        Test("推荐页：最近发布的剧集问的是单集，按播出日期排", () =>
        {
            var query = LibraryRecommended.Query("library-a", RecommendedShelf.RecentlyReleased, limit: 12);
            Assert.Equal("Episode", string.Join(',', query.IncludeItemTypes));
            Assert.Equal("PremiereDate,SortName", query.SortBy);
            Assert.True(query.Descending);
            Assert.Equal(12, query.Limit);
            Assert.Contains("IncludeItemTypes=Episode", query.ToQueryString());
        });

        Test("推荐页：继续观看不走通用查询", () =>
        {
            // 这一排整份交给服务器那条 Resume 接口（有没有播放进度、有没有被移出，通用查询都拼不出来），
            // 所以 Query() 对它只交回一个空壳，谁也别拿它去问服务器。
            var query = LibraryRecommended.Query("library-a", RecommendedShelf.ContinueWatching);
            Assert.Equal(0, query.IncludeItemTypes.Count);
            Assert.Equal("", query.SortBy);
        });

        Test("推荐页：单集的第二行是「SxxExx - 集名」", () =>
        {
            Assert.Equal("S01E07 - 敏北", LibraryRecommended.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Episode,
                Name = "敏北",
                ParentIndexNumber = 1,
                IndexNumber = 7
            }));
            // 没有集号时只写集名，没有集名时只写集号 —— 不留下一个孤零零的「 - 」。
            Assert.Equal("敏北", LibraryRecommended.Subtitle(new EmbyItem { Type = EmbyItemType.Episode, Name = "敏北" }));
            Assert.Equal("S02E05", LibraryRecommended.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Episode,
                Name = "",
                ParentIndexNumber = 2,
                IndexNumber = 5
            }));
            Assert.Null(LibraryRecommended.Subtitle(new EmbyItem { Type = EmbyItemType.Episode, Name = "" }));
        });

        Test("推荐页：剧集的第二行是年份区间", () =>
        {
            Assert.Equal("2026 - 现在", LibraryRecommended.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Series,
                Name = "幻视征途",
                ProductionYear = 2026,
                Status = "Continuing"
            }));
            Assert.Equal("2006 - 2007", LibraryRecommended.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Series,
                Name = "死亡笔记",
                ProductionYear = 2006,
                Status = "Ended",
                EndDate = new DateTimeOffset(2007, 6, 1, 0, 0, 0, TimeSpan.Zero)
            }));
            // 只有开播那一年、或者干脆没有年份：不编一个结束年份，也不写「0」。
            Assert.Equal("2019", LibraryRecommended.Subtitle(new EmbyItem
            {
                Type = EmbyItemType.Series,
                Name = "无结局",
                ProductionYear = 2019,
                Status = "Ended"
            }));
            Assert.Null(LibraryRecommended.Subtitle(new EmbyItem { Type = EmbyItemType.Series, Name = "无年份" }));
        });

        Test("推荐页：别的条目交回卡片自己的副标题", () =>
        {
            // 电影、文件夹不会出现在这三排里；真出现了也由 CardItem 的兜底那一句接手，不是空白。
            Assert.Null(LibraryRecommended.Subtitle(new EmbyItem { Type = EmbyItemType.Movie, Name = "电影", ProductionYear = 2019 }));
            Assert.Null(LibraryRecommended.Subtitle(new EmbyItem { Type = EmbyItemType.Folder, Name = "文件夹" }));
        });
    }
}
