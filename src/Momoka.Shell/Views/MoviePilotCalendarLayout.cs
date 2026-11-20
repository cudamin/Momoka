using Momoka.MoviePilot;

namespace Momoka.Shell.Views;

internal static class MoviePilotCalendarLayout
{
    internal static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);

    internal static (DateOnly Start, DateOnly EndExclusive) Window(DateOnly month)
    {
        var first = Month(month);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var next = first.AddMonths(1);
        var end = next.AddDays((7 - (((int)next.DayOfWeek + 6) % 7)) % 7);
        return (start, end);
    }

    internal static IReadOnlyList<MoviePilotCalendarEntry> Filter(IEnumerable<MoviePilotCalendarEntry> entries, int type) =>
        entries.Where(entry => type switch { 1 => entry.Type == "电影", 2 => entry.Type == "电视剧", _ => true }).ToArray();
}
