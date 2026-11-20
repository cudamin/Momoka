using Momoka.MoviePilot;

namespace Momoka.Shell.Views;

internal static class MoviePilotCalendarLayout
{
    internal const int VisibleEventCount = 5;
    internal const double CalendarMinimumWidth = 1260;
    internal static int VisibleCount(int count, bool expanded) => expanded ? Math.Max(0, count) : Math.Clamp(count, 0, VisibleEventCount);
    internal static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);

    internal static (DateOnly Start, DateOnly EndExclusive) Window(DateOnly month)
    {
        var first = Month(month);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        return (start, start.AddDays(42));
    }

    internal static IReadOnlyList<MoviePilotCalendarEntry> Filter(IEnumerable<MoviePilotCalendarEntry> entries, int type) =>
        entries.Where(entry => type switch { 1 => entry.Type == "电影", 2 => entry.Type == "电视剧", _ => true }).ToArray();
}
