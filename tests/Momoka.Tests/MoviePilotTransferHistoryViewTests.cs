using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotTransferHistoryViewTests
{
    internal static void Register()
    {
        Test("整理清单搜索：空白恢复原始记录和对象顺序", () =>
        {
            var histories = Records();
            var result = Apply(histories, "  ");
            Assert.True(result.SequenceEqual(histories));
            Assert.True(ReferenceEquals(result[0], histories[0]));
        });
        foreach (var query in new[] { "测试剧", "s01e02", "2160P", "library/episode-2", "软链接", "2026-10-28", "#2", "tmdbid=283938", "media_id=283938" })
            Test("整理清单搜索：自定义字段命中 " + query, () => Assert.True(Apply(Records(), query).Any(history => history.Id == 2)));
        Test("整理清单搜索：多个词分别命中标题和路径，不要求同一个字段", () =>
            Assert.Equal("2", Ids(Apply(Records(), "测试剧 2160P s01e02"))));
        Test("整理清单搜索：通配符和Windows路径分隔符", () =>
        {
            Assert.Equal("2", Ids(Apply(Records(), "*S01E0?.2160p.mkv")));
            Assert.Equal("2", Ids(Apply(Records(), "D:/downloads/*2160p.mkv")));
            Assert.Equal("2", Ids(Apply(Records(), "D:\\downloads\\*2160p.mkv")));
            Assert.Equal(0, Apply(Records(), "[2160p]").Count);
        });
        Test("整理清单筛选：状态独立于关键词，无匹配不伪造资源", () =>
        {
            Assert.Equal("3,2", Ids(Apply(Records(), status: MoviePilotTransferStatusFilter.Success)));
            Assert.Equal("1", Ids(Apply(Records(), status: MoviePilotTransferStatusFilter.Failed)));
            Assert.Equal("2", Ids(Apply(Records(), "2160p", MoviePilotTransferStatusFilter.Success)));
            Assert.Equal(0, Apply(Records(), "不存在").Count);
            Assert.Equal(0, Apply(Records(), "2160p", MoviePilotTransferStatusFilter.Failed).Count);
        });
        Test("整理清单排序：标题按季集数字顺序，升降序对称", () =>
        {
            Assert.Equal("1,2,3", Ids(Apply(Records(), sort: MoviePilotTransferSort.Title)));
            Assert.Equal("3,2,1", Ids(Apply(Records(), sort: MoviePilotTransferSort.Title, descending: true)));
            var rows = new[] { Records()[0] with { Id = 1, Seasons = "S10", Episodes = "E01" },
                Records()[0] with { Id = 2, Seasons = "S2", Episodes = "E10" } };
            Assert.Equal("2,1", Ids(Apply(rows, sort: MoviePilotTransferSort.Title)));
        });
        Test("整理清单排序：超长集号不溢出，缺集号在前", () =>
        {
            var row = Records()[0];
            var rows = new[] { row with { Id = 1, Episodes = "E999999999999999999999999" },
                row with { Id = 2, Episodes = "E10" }, row with { Id = 3, Episodes = "" } };
            Assert.Equal("3,2,1", Ids(Apply(rows, sort: MoviePilotTransferSort.Title)));
        });
        foreach (var (sort, expected) in new[]
        {
            (MoviePilotTransferSort.Path, "1,3,2"), (MoviePilotTransferSort.Mode, "1,3,2"),
            (MoviePilotTransferSort.Size, "1,3,2"), (MoviePilotTransferSort.Time, "3,1,2"), (MoviePilotTransferSort.Status, "1,3,2")
        })
            Test("整理清单排序：字段 " + sort + " 使用真实值并支持降序", () =>
            {
                Assert.Equal(expected, Ids(Apply(Records(), sort: sort)));
                var ascending = Apply(Records(), sort: sort);
                var descending = Apply(Records(), sort: sort, descending: true);
                if (sort != MoviePilotTransferSort.Status) Assert.True(ascending.Reverse().SequenceEqual(descending));
                else Assert.Equal("3,2,1", Ids(descending));
            });
        Test("整理清单排序：同值稳定，默认方向不重排接口顺序", () =>
        {
            var rows = Records().Select(row => row with { Size = 42 }).ToArray();
            Assert.Equal("3,1,2", Ids(Apply(rows, sort: MoviePilotTransferSort.Size)));
            Assert.Equal("3,1,2", Ids(Apply(rows, sort: MoviePilotTransferSort.Size, descending: true)));
            Assert.Equal("3,1,2", Ids(Apply(Records(), descending: true)));
        });
        Test("整理清单排序：时间比较实际时刻而不是格式，缺失大小不改写快照", () =>
        {
            var first = Records()[0] with { Id = 1, Date = "2026-10-28T08:00:00+08:00", Size = null };
            var second = Records()[0] with { Id = 2, Date = "2026-10-28T01:00:00Z", Size = 0 };
            Assert.Equal("1,2", Ids(Apply([second, first], sort: MoviePilotTransferSort.Time)));
            Assert.Equal("2,1", Ids(Apply([second, first], sort: MoviePilotTransferSort.Size)));
            Assert.Null(first.Size);
        });
    }

    private static IReadOnlyList<MoviePilotTransferHistory> Apply(IEnumerable<MoviePilotTransferHistory> histories,
        string query = "", MoviePilotTransferStatusFilter status = MoviePilotTransferStatusFilter.All,
        MoviePilotTransferSort sort = MoviePilotTransferSort.Default, bool descending = false) =>
        MoviePilotTransferHistoryView.Apply(histories, query, status, sort, descending);

    private static string Ids(IEnumerable<MoviePilotTransferHistory> histories) => string.Join(",", histories.Select(history => history.Id));

    private static MoviePilotTransferHistory[] Records() =>
    [
        Row(3) with { Episodes = "E10", Mode = "move", SourcePath = "/downloads/z-1080p.mkv", Size = 100, Date = "2026-10-26 12:00:00" },
        Row(1) with { Episodes = "E01", Mode = "copy", SourcePath = "/downloads/a-720p.mkv", Success = false, Size = null, Date = "2026-10-27 12:00:00" },
        Row(2) with { Episodes = "E02", Mode = "softlink", SourcePath = "D:\\downloads\\Series.S01E02.2160p.mkv", Size = 200, Date = "2026-10-28 12:00:00" }
    ];

    private static MoviePilotTransferHistory Row(long id) => new()
    {
        Id = id,
        Title = "测试剧",
        Type = MoviePilotTransferRequest.TypeSeries,
        Seasons = "S01",
        Success = true,
        TargetPath = "/library/episode-" + id + ".mkv",
        MediaSource = "themoviedb",
        MediaId = "283938",
        TmdbId = "283938"
    };
}
