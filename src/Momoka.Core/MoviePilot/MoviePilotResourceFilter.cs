namespace Momoka.MoviePilot;

public enum MoviePilotPromotionFilter { All, Free, Discount }
public enum MoviePilotResourceSort { Original, Site, Size, Seeders, PublishedAt }

/// <summary>过滤只作用于本次返回的资源，不重新访问站点，也不改变下载所用的原始身份。</summary>
public sealed record MoviePilotResourceFilter
{
    public string Text { get; init; } = "";
    public string Site { get; init; } = "";
    public string Resolution { get; init; } = "";
    public string Label { get; init; } = "";
    public string VideoCodec { get; init; } = "";
    public string Edition { get; init; } = "";
    public string ReleaseGroup { get; init; } = "";
    // 同维度多选取并集，不同维度取交集；多选非空时优先于旧单值，兼容版本页的预选条件。
    public IReadOnlyList<string> Sites { get; init; } = [];
    public IReadOnlyList<string> Resolutions { get; init; } = [];
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> VideoCodecs { get; init; } = [];
    public IReadOnlyList<string> Editions { get; init; } = [];
    public IReadOnlyList<string> ReleaseGroups { get; init; } = [];
    public IReadOnlyList<int> Seasons { get; init; } = [];
    public IReadOnlyList<int> Episodes { get; init; } = [];
    public IReadOnlyList<double> PromotionFactors { get; init; } = [];
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public MoviePilotPromotionFilter Promotion { get; init; }
    public MoviePilotResourceSort Sort { get; init; }
    public bool SortDescending { get; init; } = true;
    public bool SeededOnly { get; init; }
    public bool ExcludeHitAndRun { get; init; }
    public int? PublishedWithinDays { get; init; }

    public bool Matches(MoviePilotResource resource, DateTimeOffset now)
    {
        if (Seasons.Count > 0 ? !Seasons.Any(resource.Seasons.Contains) :
            Season is { } season && !resource.Seasons.Contains(season)) return false;
        // 按集筛选认两处：meta 里解析出的集号，或标题／描述里写着集号（E05、第5集这些写法，见
        // MoviePilotEpisodePattern）—— 服务器 meta 偶尔整段没解析出来，标题里明写着的不该跟着丢。
        if (Episodes.Count > 0 ? !Episodes.Any(episode => MatchesEpisode(resource, episode)) :
            Episode is { } selectedEpisode && !MatchesEpisode(resource, selectedEpisode)) return false;
        if (!MatchesValue(resource.SiteName, Site, Sites) || !MatchesValue(resource.Resolution, Resolution, Resolutions) ||
            !MatchesValue(resource.VideoCodec, VideoCodec, VideoCodecs) || !MatchesValue(resource.Edition, Edition, Editions) ||
            !MatchesValue(resource.ReleaseGroup, ReleaseGroup, ReleaseGroups)) return false;
        if (Labels.Count > 0 ? !Labels.Any(label => resource.Labels.Contains(label, StringComparer.OrdinalIgnoreCase)) :
            Label.Length > 0 && !resource.Labels.Contains(Label, StringComparer.OrdinalIgnoreCase)) return false;
        if (PromotionFactors.Count > 0 &&
            (resource.DownloadFactor is not { } factor || !PromotionFactors.Contains(factor))) return false;
        if (SeededOnly && resource.Seeders <= 0 || ExcludeHitAndRun && resource.HitAndRun) return false;
        if (Promotion == MoviePilotPromotionFilter.Free && !resource.IsFree ||
            Promotion == MoviePilotPromotionFilter.Discount && !resource.HasDiscount) return false;
        if (PublishedWithinDays is { } days &&
            (resource.PublishedAt is not { } published || published < now.AddDays(-days))) return false;

        var searchable = $"{resource.Title} {resource.DisplayTitle} {resource.Description} {resource.SiteName} {resource.Resolution} " +
            $"{resource.VideoCodec} {resource.Edition} {resource.ReleaseGroup} {resource.WebSource} {resource.ResourceEffect} {resource.LabelsText}";
        return Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => searchable.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<MoviePilotResource> Apply(IEnumerable<MoviePilotResource> resources, DateTimeOffset now)
    {
        var filtered = resources.Where(resource => Matches(resource, now));
        // 与网页卡片视图一致：默认顺序保留服务器的分组位置；字段与方向独立。
        // 缺失的体积、时间或站点留在末尾，同值使用 LINQ 的稳定排序，不翻转整个列表。
        return Sort switch
        {
            MoviePilotResourceSort.Site => SortDescending
                ? filtered.OrderBy(resource => string.IsNullOrWhiteSpace(resource.SiteName))
                    .ThenByDescending(resource => resource.SiteName, StringComparer.CurrentCultureIgnoreCase)
                : filtered.OrderBy(resource => string.IsNullOrWhiteSpace(resource.SiteName))
                    .ThenBy(resource => resource.SiteName, StringComparer.CurrentCultureIgnoreCase),
            MoviePilotResourceSort.Size => SortDescending
                ? filtered.OrderBy(resource => resource.Size <= 0).ThenByDescending(resource => resource.Size)
                : filtered.OrderBy(resource => resource.Size <= 0).ThenBy(resource => resource.Size),
            MoviePilotResourceSort.Seeders => SortDescending
                ? filtered.OrderByDescending(resource => resource.Seeders)
                : filtered.OrderBy(resource => resource.Seeders),
            MoviePilotResourceSort.PublishedAt => SortDescending
                ? filtered.OrderBy(resource => resource.PublishedAt is null).ThenByDescending(resource => resource.PublishedAt)
                : filtered.OrderBy(resource => resource.PublishedAt is null).ThenBy(resource => resource.PublishedAt),
            _ => filtered
        };
    }

    private static bool MatchesValue(string? value, string single, IReadOnlyList<string> multiple) =>
        multiple.Count > 0 ? value is not null && multiple.Contains(value, StringComparer.OrdinalIgnoreCase) :
        single.Length == 0 || string.Equals(value, single, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesEpisode(MoviePilotResource resource, int episode) => resource.Episodes.Contains(episode) ||
        MoviePilotEpisodePattern.Contains(resource.Title, episode) || MoviePilotEpisodePattern.Contains(resource.Description, episode);
}
