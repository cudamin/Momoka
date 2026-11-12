using System.Globalization;

namespace Momoka.MoviePilot;

/// <summary>订阅墙的状态分类（网页端 getSubscribeStatus）；电影是原子下载目标，恒为 All。</summary>
public enum MoviePilotSubscriptionWallStatus { All, Pending, Paused, NotStarted, Subscribing, Completed }

/// <summary>订阅墙的状态筛选；洗版是独立模式，与进度状态正交（同一订阅可同时命中）。</summary>
public enum MoviePilotSubscriptionWallFilter { All, BestVersion, Pending, Paused, NotStarted, Subscribing, Completed }

/// <summary>订阅墙的排序方式；缺失集数只有电视剧可用。</summary>
public enum MoviePilotSubscriptionSort { Custom, LastUpdate, AddTime, LackEpisode }

/// <summary>
/// 订阅墙（我的订阅）的筛选与排序，逐条移植网页端 SubscribeListView 的 displayList 规则。
/// 纯函数：快照进、顺序出，不读网络也不持状态，页面刷新与拖拽落盘共用同一套判断。
/// </summary>
public static class MoviePilotSubscriptionWall
{
    /// <summary>
    /// 根据运行状态与进度判断订阅状态：待定与暂停优先，洗版不覆盖它们；
    /// 电视剧按集数分完成/订阅中/未开始，completed_episode 由后端按订阅类型派生。
    /// </summary>
    public static MoviePilotSubscriptionWallStatus Classify(MoviePilotSubscription subscription)
    {
        if (subscription.State == "P") return MoviePilotSubscriptionWallStatus.Pending;
        if (subscription.State == "S") return MoviePilotSubscriptionWallStatus.Paused;
        if (!subscription.IsSeries) return MoviePilotSubscriptionWallStatus.All;
        if (subscription.TotalEpisodes is > 0)
        {
            if ((subscription.MissingDownloads ?? 0) == 0) return MoviePilotSubscriptionWallStatus.Completed;
            return (subscription.CompletedEpisodes ?? 0) > 0
                ? MoviePilotSubscriptionWallStatus.Subscribing : MoviePilotSubscriptionWallStatus.NotStarted;
        }
        return MoviePilotSubscriptionWallStatus.NotStarted;
    }

    /// <summary>关键词对名称做小写包含；状态筛选里洗版独立判断，其余按分类相等。</summary>
    public static bool Matches(MoviePilotSubscription subscription, string keyword, MoviePilotSubscriptionWallFilter filter)
    {
        var normalized = keyword.Trim().ToLowerInvariant();
        if (normalized.Length > 0 && !subscription.Media.Title.ToLowerInvariant().Contains(normalized, StringComparison.Ordinal)) return false;
        return filter switch
        {
            MoviePilotSubscriptionWallFilter.All => true,
            MoviePilotSubscriptionWallFilter.BestVersion => subscription.BestVersion,
            MoviePilotSubscriptionWallFilter.Pending => Classify(subscription) == MoviePilotSubscriptionWallStatus.Pending,
            MoviePilotSubscriptionWallFilter.Paused => Classify(subscription) == MoviePilotSubscriptionWallStatus.Paused,
            MoviePilotSubscriptionWallFilter.NotStarted => Classify(subscription) == MoviePilotSubscriptionWallStatus.NotStarted,
            MoviePilotSubscriptionWallFilter.Subscribing => Classify(subscription) == MoviePilotSubscriptionWallStatus.Subscribing,
            _ => Classify(subscription) == MoviePilotSubscriptionWallStatus.Completed
        };
    }

    /// <summary>排序偏好的持久化键；未知键读作 null，由调用方回退默认。</summary>
    public static string SortKey(MoviePilotSubscriptionSort sort) => sort switch
    {
        MoviePilotSubscriptionSort.Custom => "custom",
        MoviePilotSubscriptionSort.LastUpdate => "last_update",
        MoviePilotSubscriptionSort.LackEpisode => "lack_episode",
        _ => "date"
    };

    public static MoviePilotSubscriptionSort? ParseSort(string? key) => key switch
    {
        "custom" => MoviePilotSubscriptionSort.Custom,
        "last_update" => MoviePilotSubscriptionSort.LastUpdate,
        "lack_episode" => MoviePilotSubscriptionSort.LackEpisode,
        "date" => MoviePilotSubscriptionSort.AddTime,
        _ => null
    };

    /// <summary>只有电视剧订阅使用缺失集数排序；电影读到这里的一律归一成添加时间。</summary>
    public static MoviePilotSubscriptionSort NormalizeSort(MoviePilotSubscriptionSort sort, bool isSeries) =>
        !isSeries && sort == MoviePilotSubscriptionSort.LackEpisode ? MoviePilotSubscriptionSort.AddTime : sort;

    /// <summary>没有显式偏好时：存过自定义顺序就按自定义，否则按添加时间。</summary>
    public static MoviePilotSubscriptionSort DefaultSort(bool hasCustomOrder) =>
        hasCustomOrder ? MoviePilotSubscriptionSort.Custom : MoviePilotSubscriptionSort.AddTime;

    /// <summary>自定义顺序存在服务器用户配置里，电影与电视剧各一把键（与网页端 orderRequestKey 一致）。</summary>
    public static string OrderConfigKey(bool isSeries) => isSeries ? "SubscribeTvOrder" : "SubscribeMovieOrder";

    /// <summary>状态筛选的可选项与顺序，照网页端 filterOptions：电影只有基本＋待定／暂停，电视剧展开全部进度状态。</summary>
    public static IReadOnlyList<MoviePilotSubscriptionWallFilter> FilterOptions(bool isSeries) => isSeries
        ? [MoviePilotSubscriptionWallFilter.All, MoviePilotSubscriptionWallFilter.BestVersion, MoviePilotSubscriptionWallFilter.NotStarted,
            MoviePilotSubscriptionWallFilter.Subscribing, MoviePilotSubscriptionWallFilter.Pending, MoviePilotSubscriptionWallFilter.Paused,
            MoviePilotSubscriptionWallFilter.Completed]
        : [MoviePilotSubscriptionWallFilter.All, MoviePilotSubscriptionWallFilter.BestVersion,
            MoviePilotSubscriptionWallFilter.Pending, MoviePilotSubscriptionWallFilter.Paused];

    /// <summary>排序的可选项与顺序，照网页端 sortOptions：缺失集数只有电视剧有。</summary>
    public static IReadOnlyList<MoviePilotSubscriptionSort> SortOptions(bool isSeries) => isSeries
        ? [MoviePilotSubscriptionSort.Custom, MoviePilotSubscriptionSort.LastUpdate, MoviePilotSubscriptionSort.AddTime, MoviePilotSubscriptionSort.LackEpisode]
        : [MoviePilotSubscriptionSort.Custom, MoviePilotSubscriptionSort.LastUpdate, MoviePilotSubscriptionSort.AddTime];

    /// <summary>筛选项的展示词（网页端 subscribe.* 与 common.all）。</summary>
    public static string FilterLabel(MoviePilotSubscriptionWallFilter filter) => filter switch
    {
        MoviePilotSubscriptionWallFilter.All => "全部",
        MoviePilotSubscriptionWallFilter.BestVersion => "洗版中",
        MoviePilotSubscriptionWallFilter.Pending => "待定",
        MoviePilotSubscriptionWallFilter.Paused => "暂停",
        MoviePilotSubscriptionWallFilter.NotStarted => "未开始",
        MoviePilotSubscriptionWallFilter.Subscribing => "订阅中",
        _ => "订阅完成"
    };

    /// <summary>排序项的展示词（网页端 subscribe.sort.*）。</summary>
    public static string SortLabel(MoviePilotSubscriptionSort sort) => sort switch
    {
        MoviePilotSubscriptionSort.Custom => "自定义",
        MoviePilotSubscriptionSort.LastUpdate => "最后更新时间",
        MoviePilotSubscriptionSort.LackEpisode => "缺失集数",
        _ => "添加时间"
    };

    /// <summary>
    /// 拖拽排序只在「自定义排序 + 无关键词 + 状态全部 + 非批量」时可用；
    /// 其余语境下排序结果不代表完整顺序，落盘会把没显示的订阅挤到末尾。
    /// </summary>
    public static bool CanDragSort(MoviePilotSubscriptionSort effectiveSort, string keyword,
        MoviePilotSubscriptionWallFilter filter, bool batchMode) =>
        effectiveSort == MoviePilotSubscriptionSort.Custom && keyword.Trim().Length == 0
        && filter == MoviePilotSubscriptionWallFilter.All && !batchMode;

    /// <summary>
    /// 时间字段转可排序毫秒数：先按 ISO 解析，再按「年/月/日」兼容格式解析（网页端 getSubscribeTimeValue），
    /// 都解析不出来记 0，排在倒序末尾。
    /// </summary>
    public static long TimeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var direct))
            return direct.ToUnixTimeMilliseconds();
        return DateTimeOffset.TryParse(value.Replace('-', '/'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var compatible)
            ? compatible.ToUnixTimeMilliseconds() : 0;
    }

    /// <summary>
    /// 按当前排序选项排序。自定义顺序里没登记的订阅排到最后，再按添加时间倒序补齐；
    /// 并列时保持原相对顺序（稳定排序，与网页端一致）。
    /// </summary>
    public static IReadOnlyList<MoviePilotSubscription> Sort(IReadOnlyList<MoviePilotSubscription> items,
        MoviePilotSubscriptionSort sort, IReadOnlyList<int> order)
    {
        if (sort != MoviePilotSubscriptionSort.Custom)
            return sort switch
            {
                MoviePilotSubscriptionSort.LastUpdate => items.OrderByDescending(item => TimeValue(item.LastUpdate)).ToArray(),
                MoviePilotSubscriptionSort.LackEpisode => items.OrderByDescending(item => item.MissingDownloads ?? 0)
                    .ThenByDescending(item => TimeValue(item.CreatedAt)).ToArray(),
                _ => items.OrderByDescending(item => TimeValue(item.CreatedAt)).ToArray()
            };
        var index = new Dictionary<int, int>();
        for (var position = 0; position < order.Count; position++) index[order[position]] = position;
        return items.OrderBy(item => index.TryGetValue(item.Id, out var position) ? position : int.MaxValue)
            .ThenByDescending(item => TimeValue(item.CreatedAt)).ToArray();
    }
}
