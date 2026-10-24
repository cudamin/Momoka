namespace Momoka.MoviePilot;

public enum MoviePilotSubscribeMode { Normal, Episodes, FullSeason }
public enum MoviePilotSubscribeChangeKind { Add, Update, Delete }
/// <summary>订阅身份必须包含来源、类型、编号、季及剧集组；标题和跨库编号不是取消依据。</summary>
public readonly record struct MoviePilotSubscriptionIdentity(string Source, string Type, string Id, int? Season, string EpisodeGroup)
{
    public static MoviePilotSubscriptionIdentity Of(MoviePilotMedia media, string group) =>
        new((media.MediaSource ?? "").ToLowerInvariant(), media.Type ?? "", media.MediaId ?? "", media.Season, group);
    public static MoviePilotSubscriptionIdentity Of(MoviePilotSubscription subscription) => Of(subscription.Media, subscription.EpisodeGroup);
}

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
    public static bool Matches(MoviePilotMedia media, string group, MoviePilotSubscription subscription) =>
        SameMedia(media, subscription.Media) && MoviePilotSubscriptionIdentity.Of(media, group) == MoviePilotSubscriptionIdentity.Of(subscription);

    public static MoviePilotSubscription? Find(MoviePilotMedia media, string group, IReadOnlyList<MoviePilotSubscription> subscriptions)
    {
        var matches = subscriptions.Where(item => Matches(media, group, item)).ToArray();
        if (matches.Length > 1) throw new MoviePilotException("同一来源、类型、季和剧集组存在多条订阅，请先在 MoviePilot 核对");
        return matches.SingleOrDefault();
    }

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
        ValidateMedia(media, group);
        if (media.Type == "电影" && choices.Count != 1) throw new MoviePilotException("电影订阅只能包含一个目标");
        if (choices.GroupBy(choice => choice.Season).Any(items => items.Count() > 1)) throw new MoviePilotException("季选择重复");
        var plan = new List<MoviePilotSubscribeChange>();
        foreach (var choice in choices)
        {
            if (!Enum.IsDefined(choice.Mode) || (media.Type == "电视剧" ? choice.Season is null or < 0 : choice.Season is not null))
                throw new MoviePilotException("订阅季号或模式不正确");
            var target = media with { Season = choice.Season };
            var current = Find(target, group, existing);
            if (choice.Selected && current is null) plan.Add(new(MoviePilotSubscribeChangeKind.Add, target, group, choice.Mode, null));
            else if (!choice.Selected && current is not null) plan.Add(new(MoviePilotSubscribeChangeKind.Delete, target, current.EpisodeGroup, Mode(current), current));
            else if (choice.Selected && current is not null && Mode(current) != choice.Mode)
                plan.Add(new(MoviePilotSubscribeChangeKind.Update, target, current.EpisodeGroup, choice.Mode, current));
        }
        return plan;
    }

    public static void Validate(MoviePilotSubscribeChange change)
    {
        ValidateMedia(change.Media, change.EpisodeGroup);
        if (!Enum.IsDefined(change.Kind) || !Enum.IsDefined(change.Mode)
            || (change.Media.Type == "电视剧" ? change.Media.Season is null or < 0 : change.Media.Season is not null))
            throw new MoviePilotOperationBlockedException("订阅季号或模式不正确");
        if (change.Kind == MoviePilotSubscribeChangeKind.Add)
        {
            if (change.Original is not null) throw new MoviePilotOperationBlockedException("新增订阅不能携带其他订阅编号");
        }
        else if (change.Original is not { Id: > 0 } original || !Matches(change.Media, change.EpisodeGroup, original))
            throw new MoviePilotOperationBlockedException("订阅编号与所选来源、类型、季或剧集组不匹配，不能修改或取消");
    }

    private static void ValidateMedia(MoviePilotMedia media, string group)
    {
        if (!media.CanSubscribe || media.Type is not ("电视剧" or "电影")) throw new MoviePilotException("缺少可信的媒体身份");
        if (group.Length > 0 && (media.Type != "电视剧" || !string.Equals(media.MediaSource, "themoviedb", StringComparison.OrdinalIgnoreCase)
            || media.MediaId?.All(char.IsAsciiDigit) != true))
            throw new MoviePilotOperationBlockedException("此媒体不支持 TMDB 剧集组");
    }

    public static IReadOnlyDictionary<string, object?> Body(MoviePilotSubscribeChange change)
    {
        Validate(change);
        if (change.Kind == MoviePilotSubscribeChangeKind.Delete) throw new MoviePilotOperationBlockedException("取消订阅只按明确订阅编号执行");
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
