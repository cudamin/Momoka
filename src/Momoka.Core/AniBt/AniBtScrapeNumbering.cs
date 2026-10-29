using System.Globalization;
using System.Text.RegularExpressions;
using Momoka.MoviePilot;

namespace Momoka.AniBt;

/// <summary>纠正作品身份时保留现有季集；不按 Bangumi 的总集数拆季或减去前季集数。</summary>
internal sealed partial record AniBtScrapeNumbering(int? Season, string Episode)
{
    internal string Label => Season is { } season && Episode.Length > 0
        && int.TryParse(Episode, NumberStyles.None, CultureInfo.InvariantCulture, out var episode)
            ? $"S{season:00}E{episode:00}" : "";

    internal static AniBtScrapeNumbering Resolve(AniBtScrapeRow row, int? season, string episode)
    {
        var numbered = SeasonEpisode().Match(MoviePilotTransfer.PathName(row.TargetPath));
        var historySeason = HistorySeason().Match(row.History?.Seasons ?? "");
        var historyEpisode = HistoryEpisode().Match(row.History?.Episodes ?? "");
        var sourceEpisode = ReleaseEpisode().Match(row.SourceName);
        season ??= Number(numbered, "season") ?? Number(historySeason, "season");
        if (episode.Length == 0)
        {
            var number = Number(numbered, "episode") ?? Number(historyEpisode, "episode")
                ?? Number(sourceEpisode, "episode");
            episode = number?.ToString(CultureInfo.InvariantCulture) ?? "";
        }
        return new(season, episode);
    }

    private static int? Number(Match match, string group) => match.Success
        && int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value : null;

    [GeneratedRegex(@"(?<![0-9A-Za-z])S(?<season>\d{1,4})E(?<episode>\d{1,4})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(@"^S(?<season>\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HistorySeason();

    [GeneratedRegex(@"^E(?<episode>\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HistoryEpisode();

    // 只认发布标题里的独立集号，不把 1080p、10bit、年份或 ASSx2 当集号。
    [GeneratedRegex(@"\s-\s(?<episode>\d{1,4})(?=\s*\[|\.[A-Za-z0-9]+$|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseEpisode();
}
