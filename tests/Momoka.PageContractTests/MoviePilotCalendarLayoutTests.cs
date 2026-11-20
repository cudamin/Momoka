using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotCalendarLayoutTests
{
    internal static void Register()
    {
        Test("MP 日历日期：整月覆盖从周一开始，末日不包含", () =>
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
                    Assert.True(end > last && end.AddDays(-7) <= last, first + " 月末没有落在最后一周");
                    Assert.True(end.DayNumber - start.DayNumber is 28 or 35 or 42, first + " 多出或少了一周");
                    Assert.Equal(first, MoviePilotCalendarLayout.Month(first.AddDays(14)));
                }
        });

        Test("MP 日历日期：28 天二月、闰日与跨年边格保留真实日期", () =>
        {
            Assert.Equal((new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 1)),
                MoviePilotCalendarLayout.Window(new DateOnly(2027, 2, 15)));
            Assert.Equal((new DateOnly(2024, 1, 29), new DateOnly(2024, 3, 4)),
                MoviePilotCalendarLayout.Window(new DateOnly(2024, 2, 29)));
            Assert.Equal((new DateOnly(2026, 12, 28), new DateOnly(2027, 2, 1)),
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
