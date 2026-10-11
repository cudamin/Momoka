namespace Momoka.MoviePilot;

public enum MoviePilotSubscribeMode { Normal, Episodes, FullSeason }
public enum MoviePilotSubscribeChangeKind { Add, Update, Delete }
public sealed record MoviePilotSeasonChoice(int? Season, bool Selected, MoviePilotSubscribeMode Mode);
public sealed record MoviePilotSubscribeChange(MoviePilotSubscribeChangeKind Kind, MoviePilotMedia Media,
    string EpisodeGroup, MoviePilotSubscribeMode Mode, MoviePilotSubscription? Original)
{
    public string Label => Description;
    public string Description => (Media.Season is { } season ? $"第 {season} 季" : Media.Title) + " · " +
        (Kind == MoviePilotSubscribeChangeKind.Delete ? "取消订阅" : MoviePilotSubscriptionPlan.ModeLabel(Mode));
}

/// <summary>季选择只生成当前组的差量；同季的其他剧集组与未显示季不受影响。</summary>
public static class MoviePilotSubscriptionPlan
{
    public static bool SameMedia(MoviePilotMedia left, MoviePilotMedia right) =>
        left.CanSubscribe && right.CanSubscribe && string.Equals(left.MediaSource, right.MediaSource, StringComparison.OrdinalIgnoreCase)
        && left.MediaId == right.MediaId && left.Type == right.Type;
    public static MoviePilotSubscribeMode Mode(MoviePilotSubscription item) => !item.BestVersion ? MoviePilotSubscribeMode.Normal
        : MoviePilotSubscription.Number(item.Data, "best_version_full") == 1 ? MoviePilotSubscribeMode.FullSeason : MoviePilotSubscribeMode.Episodes;
    public static string ModeLabel(MoviePilotSubscribeMode mode) => mode switch
    {
        MoviePilotSubscribeMode.Normal => "普通订阅",
        MoviePilotSubscribeMode.Episodes => "分集洗版",
        _ => "全集洗版"
    };

    public static IReadOnlyList<MoviePilotSubscribeChange> Build(MoviePilotMedia media, string group,
        IReadOnlyList<MoviePilotSeasonChoice> choices, IReadOnlyList<MoviePilotSubscription> existing)
    {
        if (!media.CanSubscribe || media.Type is not ("电视剧" or "电影")) throw new MoviePilotException("缺少可信的媒体身份");
        if (choices.GroupBy(choice => choice.Season).Any(items => items.Count() > 1)) throw new MoviePilotException("季选择重复");
        var plan = new List<MoviePilotSubscribeChange>();
        foreach (var choice in choices)
        {
            if (!Enum.IsDefined(choice.Mode) || (media.Type == "电视剧" ? choice.Season is null or < 0 : choice.Season is not null))
                throw new MoviePilotException("订阅季号或模式不正确");
            var matches = existing.Where(item => SameMedia(media, item.Media) && item.Media.Season == choice.Season
                && item.EpisodeGroup == group).ToArray();
            if (matches.Length > 1) throw new MoviePilotException("同一季存在多条订阅，请先在 MoviePilot 核对");
            var current = matches.SingleOrDefault();
            var target = media with { Season = choice.Season };
            if (choice.Selected && current is null) plan.Add(new(MoviePilotSubscribeChangeKind.Add, target, group, choice.Mode, null));
            else if (!choice.Selected && current is not null) plan.Add(new(MoviePilotSubscribeChangeKind.Delete, target, current.EpisodeGroup, Mode(current), current));
            else if (choice.Selected && current is not null && Mode(current) != choice.Mode)
                plan.Add(new(MoviePilotSubscribeChangeKind.Update, target, current.EpisodeGroup, choice.Mode, current));
        }
        return plan;
    }

    public static IReadOnlyDictionary<string, object?> Body(MoviePilotSubscribeChange change)
    {
        var body = change.Kind == MoviePilotSubscribeChangeKind.Add
            ? new Dictionary<string, object?>(MoviePilotMediaParser.SubscribeBody(change.Media))
            : new Dictionary<string, object?> { ["id"] = change.Original?.Id ?? throw new MoviePilotException("订阅已失效") };
        if (change.Kind == MoviePilotSubscribeChangeKind.Add)
        {
            body["episode_group"] = change.EpisodeGroup;
            body["season"] = change.Media.Season;
        }
        body["best_version"] = change.Mode == MoviePilotSubscribeMode.Normal ? 0 : 1;
        body["best_version_full"] = change.Mode == MoviePilotSubscribeMode.FullSeason ? 1 : 0;
        return body;
    }
}
