namespace Momoka.Shell.Views;

internal static class MoviePilotCalendarLayout
{
    internal const int VisibleEventCount = 5;
    internal const double TextOnlyCardWidth = 62;
    internal static (bool Stacked, double PosterWidth, double Gap, double Padding) FitCard(double width)
    {
        width = double.IsFinite(width) ? Math.Max(0, width) : 0;
        var stacked = width < 120;
        var padding = width < 100 ? 3d : 6d;
        if (width < TextOnlyCardWidth) return (true, 0, 0, padding);
        var poster = stacked ? Math.Clamp(width * 0.5, 0, 56) : Math.Clamp(width * 0.28, 32, 56);
        return (stacked, Math.Min(poster, width - (2 * padding)), width < 180 ? 4 : 8, padding);
    }
    internal static int VisibleCount(int count, bool expanded) => expanded ? Math.Max(0, count) : Math.Clamp(count, 0, VisibleEventCount);
    internal static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);

    internal static (DateOnly Start, DateOnly EndExclusive) Window(DateOnly month)
    {
        var first = Month(month);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        return (start, start.AddDays(42));
    }

    internal static IReadOnlyList<T> Filter<T>(IEnumerable<T> entries, int type, Func<T, string> mediaType) =>
        entries.Where(entry => type switch { 1 => mediaType(entry) == "电影", 2 => mediaType(entry) == "电视剧", _ => true }).ToArray();
}
