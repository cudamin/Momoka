using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotCalendarLayoutTests
{
    internal static void Register()
    {
        Test("MP 日历日期：每月固定六周，从周一开始且末日不包含", () =>
        {
            foreach (var year in new[] { 1999, 2000, 2024, 2026, 2027, 2100 })
                for (var month = 1; month <= 12; month++)
                {
                    var first = new DateOnly(year, month, 1);
                    var (start, end) = MoviePilotCalendarLayout.Window(first.AddDays(14));
                    Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
                    Assert.Equal(DayOfWeek.Monday, end.DayOfWeek);
                    Assert.True(start <= first && start.AddDays(7) > first, first + " 月首没有落在第一周");
                    var last = first.AddMonths(1).AddDays(-1);
                    Assert.True(end > last, first + " 月末没有出现在六周窗口内");
                    Assert.Equal(42, end.DayNumber - start.DayNumber, first + " 没有保留六周的稳定高度");
                    Assert.Equal(first, MoviePilotCalendarLayout.Month(first.AddDays(14)));
                }
        });

        Test("MP 日历日期：28 天二月、闰日与跨年边格保留真实日期", () =>
        {
            Assert.Equal((new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 15)),
                MoviePilotCalendarLayout.Window(new DateOnly(2027, 2, 15)));
            Assert.Equal((new DateOnly(2024, 1, 29), new DateOnly(2024, 3, 11)),
                MoviePilotCalendarLayout.Window(new DateOnly(2024, 2, 29)));
            Assert.Equal((new DateOnly(2026, 12, 28), new DateOnly(2027, 2, 8)),
                MoviePilotCalendarLayout.Window(new DateOnly(2027, 1, 15)));
            Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 7)),
                MoviePilotCalendarLayout.Window(new DateOnly(2026, 11, 20)));
        });

        Test("MP 日历筛选：全部、电影、电视剧不丢身份与日期", () =>
        {
            var entries = new[]
            {
                Entry(1, "电影", new(2026, 11, 20)),
                Entry(2, "电视剧", new(2026, 11, 20)),
                Entry(3, "电视剧", new(2026, 11, 21)),
            };
            var all = MoviePilotCalendarLayout.Filter(entries, 0);
            Assert.Equal(3, all.Count);
            Assert.True(entries.SequenceEqual(all));
            var movies = MoviePilotCalendarLayout.Filter(entries, 1);
            Assert.Equal(1, movies.Count);
            Assert.True(ReferenceEquals(entries[0], movies[0]));
            var series = MoviePilotCalendarLayout.Filter(entries, 2);
            Assert.Equal(2, series.Count);
            Assert.True(ReferenceEquals(entries[1], series[0]) && ReferenceEquals(entries[2], series[1]));
            Assert.Equal(0, MoviePilotCalendarLayout.Filter(Array.Empty<MoviePilotCalendarEntry>(), 0).Count);
            Assert.Equal(3, entries.Length, "筛选不能改写服务快照");
        });

        Test("MP 日历日格：前五项直接展示，更多在当天展开后全部保留", () =>
        {
            Assert.Equal(5, MoviePilotCalendarLayout.VisibleEventCount);
            foreach (var count in new[] { 0, 1, 4, 5, 6, 10, 100, int.MaxValue })
            {
                Assert.Equal(Math.Min(count, 5), MoviePilotCalendarLayout.VisibleCount(count, expanded: false));
                Assert.Equal(count, MoviePilotCalendarLayout.VisibleCount(count, expanded: true));
            }
            Assert.Equal(0, MoviePilotCalendarLayout.VisibleCount(-1, expanded: false));
            Assert.Equal(0, MoviePilotCalendarLayout.VisibleCount(-1, expanded: true));
        });

        Test("日历自适应：窄格海报等比收紧并在文字上方重排", () =>
        {
            foreach (var width in new[] { 0d, 20, 42, 60, 99, 100, 149.9, 150, 179.9, 180, 200, 400 })
            {
                var layout = MoviePilotCalendarLayout.FitCard(width);
                Assert.Equal(width < 150, layout.Stacked);
                Assert.True(layout.PosterWidth >= 0 && layout.PosterWidth <= 56);
                Assert.True(layout.PosterWidth + (2 * layout.Padding) <= Math.Max(width, 2 * layout.Padding));
                if (!layout.Stacked) Assert.True(layout.PosterWidth + layout.Gap + (2 * layout.Padding) < width);
            }
            Assert.Equal(56d, MoviePilotCalendarLayout.FitCard(200).PosterWidth);
            Assert.Equal(30d, MoviePilotCalendarLayout.FitCard(60).PosterWidth);
            foreach (var width in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1 })
                Assert.Equal(0d, MoviePilotCalendarLayout.FitCard(width).PosterWidth);
        });
    }

    private static MoviePilotCalendarEntry Entry(int id, string type, DateOnly date) => new()
    {
        Date = date,
        SubscriptionId = id,
        Title = "匿名订阅 " + id,
        Type = type,
        ConnectionStamp = "fixture-connection",
    };
}
