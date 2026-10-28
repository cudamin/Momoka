using System.Text.Json;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotResourceSortTests
{
    public static void Register()
    {
        foreach (var descending in new[] { false, true })
        {
            var direction = descending;
            foreach (var field in new[] { MoviePilotResourceSort.Site, MoviePilotResourceSort.Size,
                MoviePilotResourceSort.Seeders, MoviePilotResourceSort.PublishedAt })
            {
                var sort = field;
                Test($"MoviePilot 网页排序：{sort} {(direction ? "降序" : "升序")}，同值稳定且原身份不变", () =>
                {
                    var rows = Resources();
                    var filter = new MoviePilotResourceFilter { Sort = sort, SortDescending = direction };
                    var sorted = filter.Apply(rows, DateTimeOffset.Now).ToArray();
                    var expected = direction ? "large,middle,tie,small" : "small,middle,tie,large";
                    Assert.Equal(expected, string.Join(',', sorted.Select(row => row.Title)));
                    Assert.True(ReferenceEquals(sorted.Single(row => row.Title == "middle"), rows[0]));
                    Assert.True(ReferenceEquals(sorted.Single(row => row.Title == "tie"), rows[2]));
                    Assert.Equal("middle,small,tie,large", string.Join(',', rows.Select(row => row.Title)), "不修改输入或下载资源身份");
                    Assert.Equal(expected, string.Join(',', filter.Apply(rows, DateTimeOffset.Now).Select(row => row.Title)));
                });
            }
            Test($"MoviePilot 网页默认排序：方向={direction} 仍保留服务器分组顺序", () =>
            {
                var rows = Resources();
                var sorted = new MoviePilotResourceFilter { SortDescending = direction }.Apply(rows, DateTimeOffset.Now).ToArray();
                Assert.Equal("middle,small,tie,large", string.Join(',', sorted.Select(row => row.Title)));
                Assert.True(rows.Zip(sorted).All(pair => ReferenceEquals(pair.First, pair.Second)));
            });
            foreach (var field in new[] { MoviePilotResourceSort.Site, MoviePilotResourceSort.Size, MoviePilotResourceSort.PublishedAt })
            {
                var sort = field;
                Test($"MoviePilot 网页排序：{sort} 方向={direction} 缺失元数据两项稳定排后", () =>
                {
                    var known = Resources();
                    var missing = new MoviePilotResource { Title = "missing", TorrentInfo = JsonSerializer.SerializeToElement(new { }) };
                    var missing2 = missing with { Title = "missing2" };
                    var rows = new[] { missing, known[0], missing2, known[1] };
                    var sorted = new MoviePilotResourceFilter { Sort = sort, SortDescending = direction }.Apply(rows, DateTimeOffset.Now).ToArray();
                    Assert.Equal(direction ? "middle,small,missing,missing2" : "small,middle,missing,missing2",
                        string.Join(',', sorted.Select(row => row.Title)));
                });
            }
        }
        Test("MoviePilot 网页排序：先过滤再排序，同站筛选不改变资源身份", () =>
        {
            var rows = Resources();
            var filter = new MoviePilotResourceFilter { Sites = ["B"], Sort = MoviePilotResourceSort.Seeders, SortDescending = false };
            var sorted = filter.Apply(rows, DateTimeOffset.Now).ToArray();
            Assert.Equal("middle,tie", string.Join(',', sorted.Select(row => row.Title)));
            Assert.True(ReferenceEquals(rows[0], sorted[0]) && ReferenceEquals(rows[2], sorted[1]));
        });
    }

    private static MoviePilotResource[] Resources()
    {
        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var middle = new MoviePilotResource
        {
            Title = "middle", SiteName = "B", Size = 20, Seeders = 20, PublishedAt = now.AddDays(-2),
            TorrentInfo = JsonSerializer.SerializeToElement(new { title = "anonymous" })
        };
        return [middle, middle with { Title = "small", SiteName = "A", Size = 10, Seeders = 10, PublishedAt = now.AddDays(-3) },
            middle with { Title = "tie" }, middle with { Title = "large", SiteName = "C", Size = 30, Seeders = 30, PublishedAt = now.AddDays(-1) }];
    }
}
