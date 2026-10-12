using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public enum MoviePilotSubscriptionAction { Pause, Resume, Search, Reset, Delete }

public sealed record MoviePilotSubscription
{
    public required int Id { get; init; }
    public required MoviePilotMedia Media { get; init; }
    public required JsonElement Data { get; init; }
    internal string ConnectionStamp { get; init; } = "";
    public bool IsSeries => Media.Type == "电视剧";
    public string Title => Media.Display + (IsSeries && Media.Season is { } season ? $" · 第 {season} 季" : "");
    public string State => Text(Data, "state");
    public bool Paused => State == "S";
    public int? TotalEpisodes => Number(Data, "total_episode");
    public int? MissingDownloads => Number(Data, "lack_episode");
    public int StartEpisode => Math.Max(1, Number(Data, "start_episode") ?? 1);
    public string StateLabel => State switch { "S" => "已暂停", "R" => "订阅中", "N" or "P" => "待处理", _ => "状态未知" };
    public string ProgressCount => IsSeries && TotalEpisodes is > 0 && MissingDownloads is >= 0
        ? $"{Math.Clamp(TotalEpisodes.Value - MissingDownloads.Value, 0, TotalEpisodes.Value)} / {TotalEpisodes} 集"
        : "";
    public string ProgressLabel => ProgressCount.Length > 0 ? "订阅进度 " + ProgressCount : "";

    /// <summary>
    /// 订阅详情「订阅信息」「订阅规则」两栏读的那些字段。全部只读 <see cref="Data"/> 快照：服务器没给的字段
    /// 一律留空串，屏上就不出现那一行，而不是显示一个「无」。这里是原值出口，怎么排版由
    /// <see cref="MoviePilotSubscriptionFacts"/> 决定。
    /// </summary>
    public double? Vote => Real(Data, "vote");
    public string Keyword => Text(Data, "keyword");
    public string Include => Text(Data, "include");
    public string Exclude => Text(Data, "exclude");
    public string Quality => Text(Data, "quality");
    public string Resolution => Text(Data, "resolution");
    public string Effect => Text(Data, "effect");
    public string Filter => Text(Data, "filter");
    public string CustomWords => Text(Data, "custom_words");
    public string SavePath => Text(Data, "save_path");
    public string Downloader => Text(Data, "downloader");
    public string Username => Text(Data, "username");
    public string CreatedAt => Text(Data, "date");
    public string LastUpdate => Text(Data, "last_update");
    public string LastSearch => Text(Data, "last_search");
    public int? SearchInterval => Number(Data, "search_interval");
    public int? CompletedEpisodes => Number(Data, "completed_episode");

    /// <summary>洗版开关（<c>best_version</c> 是 0/1 的数字，不是布尔）。</summary>
    public bool BestVersion => Number(Data, "best_version") is > 0;

    /// <summary>订阅范围里的站点个数。服务器不给 <c>sites</c> 时是 0，屏上就不显示这一行。</summary>
    public int SiteCount => Member(Data, "sites") is { ValueKind: JsonValueKind.Array } sites ? sites.GetArrayLength() : 0;

    /// <summary>
    /// TMDB 剧集组（<c>episode_group</c>）。有它时那一季的集号按剧集组算，取播出日期要一并带上，否则集号对不上。
    /// 服务器不给就是空串，查询里也不带这个参数。
    /// </summary>
    public string EpisodeGroup => Text(Data, "episode_group");

    public static IReadOnlyList<MoviePilotSubscription> ParseList(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array)
            throw new MoviePilotException("MoviePilot 订阅列表格式不正确，请刷新重试");
        return data.EnumerateArray().Select(Parse).Where(item => item is not null)
            .Cast<MoviePilotSubscription>().DistinctBy(item => item.Id).ToArray();
    }

    public static MoviePilotSubscription? Parse(JsonElement item)
    {
        if (Number(item, "id") is not > 0 || item.ValueKind != JsonValueKind.Object) return null;
        var type = Text(item, "type");
        type = type.ToLowerInvariant() switch { "movie" => "电影", "tv" or "series" => "电视剧", _ => type };
        if (type is not ("电影" or "电视剧")) return null;
        return new MoviePilotSubscription
        {
            Id = Number(item, "id")!.Value,
            Data = item.Clone(),
            Media = new MoviePilotMedia
            {
                Title = Text(item, "name") is { Length: > 0 } name ? name : "未命名订阅",
                Year = Number(item, "year"),
                Season = Number(item, "season"),
                Type = type,
                MediaSource = Text(item, "media_source"),
                MediaId = Text(item, "media_id"),
                PosterUrl = Text(item, "poster"),
                BackdropUrl = Text(item, "backdrop"),
                Overview = Text(item, "description")
            }
        };
    }

    internal static JsonElement Member(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value : default;
    public static string Text(JsonElement item, string name) => Scalar(Member(item, name));
    internal static string Scalar(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.String => item.GetString() ?? "",
        JsonValueKind.Number => item.GetRawText(),
        _ => ""
    };
    public static int? Number(JsonElement item, string name) =>
        int.TryParse(Text(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// 浮点读数（<c>vote</c>）。MoviePilot 的评分是 JSON 数字，但同一个字段在别的版本里也可能是字符串
    /// （和体积、优惠因子一个路数），所以走文本再解析，两种都收。
    /// </summary>
    public static double? Real(JsonElement item, string name) =>
        double.TryParse(Text(item, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// 时间戳排到「年-月-日 时:分」。服务器给的是 ISO 串，直接摊在屏上是 26 个字符还带 <c>T</c> 和秒；
    /// 解析不出来（换了格式）就原样带出，不吞掉信息。
    /// </summary>
    public static string Stamp(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : value;
}

/// <summary>订阅详情里的一行：左边标签、右边值。空值不进列表 —— 见 <see cref="MoviePilotSubscriptionFacts"/>。</summary>
public sealed record MoviePilotSubscriptionFact(string Label, string Value);

/// <summary>
/// 订阅详情两栏的投影，纯函数：一份订阅快照进，两串「标签 / 值」出。放在 Core 而不是页面里，是因为
/// 「哪些字段算订阅信息、哪些算订阅规则、什么算空」是协议侧的判断，与排版无关，也才测得动。
/// <para>
/// 分工照参考页：<see cref="Info"/> 是这条订阅自己的记录（编号、状态、年份、集数进度、搜索周期、时间戳、
/// 站点数、下载器、保存路径、洗版、订阅用户），<see cref="Rules"/> 是搜索与过滤规则（关键词、包含、排除、
/// 质量、分辨率、特效、过滤规则、自定义识别词）。**媒体身份（来源／编号／类型／季号）不在这两栏里** ——
/// 那是标题下面那一行的活（见页面里的 hero 元信息），两处都写就成了同一件事说两遍。
/// </para>
/// <para>
/// 全部只显示服务器确实给了的字段：空值留白，不写「无」。
/// </para>
/// </summary>
public static class MoviePilotSubscriptionFacts
{
    public static IReadOnlyList<MoviePilotSubscriptionFact> Info(MoviePilotSubscription subscription)
    {
        var facts = new List<MoviePilotSubscriptionFact>
        {
            new("订阅编号", subscription.Id.ToString(CultureInfo.InvariantCulture)),
            // 「订阅状态」而不是「状态」：这一列现在和媒体信息那一列同住一张卡（用户令 2026-10-12，
            // 照网页端把两段并成一列），而媒体信息里已经有一行「状态」说的是 TMDB 的状态
            //（Returning Series / Released），两个「状态」并排看不出哪个是哪个。
            new("订阅状态", subscription.StateLabel)
        };
        if (subscription.Media.Year is { } year) Add(facts, "年份", year.ToString(CultureInfo.InvariantCulture));
        if (subscription.IsSeries)
        {
            if (subscription.TotalEpisodes is > 0) Add(facts, "总集数", $"{subscription.TotalEpisodes} 集");
            if (subscription.StartEpisode > 1) Add(facts, "起始集", $"第 {subscription.StartEpisode} 集");
            if (subscription.CompletedEpisodes is { } completed) Add(facts, "已完成", $"{completed} 集");
            Add(facts, "订阅进度", subscription.ProgressCount);
        }
        if (subscription.SearchInterval is { } interval) Add(facts, "搜索间隔", $"每 {interval} 小时");
        Add(facts, "上次搜索", MoviePilotSubscription.Stamp(subscription.LastSearch));
        Add(facts, "上次更新", MoviePilotSubscription.Stamp(subscription.LastUpdate));
        Add(facts, "创建时间", MoviePilotSubscription.Stamp(subscription.CreatedAt));
        if (subscription.SiteCount > 0) Add(facts, "搜索站点", $"{subscription.SiteCount} 个");
        Add(facts, "下载器", subscription.Downloader);
        Add(facts, "保存路径", subscription.SavePath);
        if (subscription.BestVersion) Add(facts, "洗版", "开启");
        Add(facts, "订阅用户", subscription.Username);
        return facts;
    }

    public static IReadOnlyList<MoviePilotSubscriptionFact> Rules(MoviePilotSubscription subscription)
    {
        var facts = new List<MoviePilotSubscriptionFact>();
        Add(facts, "搜索关键词", subscription.Keyword);
        Add(facts, "包含规则", subscription.Include);
        Add(facts, "排除规则", subscription.Exclude);
        Add(facts, "质量规则", subscription.Quality);
        Add(facts, "分辨率规则", subscription.Resolution);
        Add(facts, "特效规则", subscription.Effect);
        Add(facts, "过滤规则", subscription.Filter);
        Add(facts, "自定义识别词", subscription.CustomWords);
        return facts;
    }

    private static void Add(List<MoviePilotSubscriptionFact> facts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) facts.Add(new(label, value.Trim()));
    }
}

public sealed record MoviePilotSubscriptionEpisode(int Number, string Title, string Description,
    IReadOnlyList<string> LibraryFiles, IReadOnlyList<string> DownloadFiles)
{
    public string ArtworkUrl { get; init; } = "";
    public IReadOnlyList<MoviePilotSubscriptionLibraryFile> LibraryEntries { get; init; } = [];

    /// <summary>
    /// 这一集的播出日期。订阅的文件统计（<c>subscribe/files/{id}</c>）不带它 —— 那个 schema 里没有播出日期这一项，
    /// 所以由 <see cref="MoviePilotEpisodeAirDates"/> 从 TMDB 季集接口取回后补上（只对来源为 TMDB 的订阅有效）。
    /// 取不到就是 null，屏上那一行不出现，也不判「已播出」。
    /// </summary>
    public DateTimeOffset? AirDate { get; init; }

    public bool InLibrary => LibraryFiles.Count > 0;
    public string Label => Number == 0 ? "电影" : $"第 {Number} 集";
    public string Status => InLibrary ? "已入库" : DownloadFiles.Count > 0 ? "已下载 · 待入库" : "缺集";

    /// <summary>
    /// 播出日期那一行：已经播出的写「播出 yyyy-MM-dd」，还没播的写「待播出 yyyy-MM-dd」，没有日期就是空串。
    /// 「已播出」按当地当前时间比 —— TMDB 的 <c>air_date</c> 只有日期没有时刻，取当天零点，所以当天也算已播出。
    /// </summary>
    public string AirDateLabel => AirDate is not { } date ? ""
        : date <= DateTimeOffset.Now
            ? "播出 " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "待播出 " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>已经播出（有日期且不晚于现在）。屏上「搜索这一集的资源」只给已播出的集 —— 没播的搜也搜不到。</summary>
    public bool HasAired => AirDate is { } date && date <= DateTimeOffset.Now;

    public string Files => string.Join('\n', LibraryFiles.Select(file => "入库：" + file)
        .Concat(DownloadFiles.Select(file => "下载：" + file)));
}

public sealed record MoviePilotSubscriptionLibraryFile(string Path, string ServerType, string ItemId);

public sealed record MoviePilotSubscriptionFiles(IReadOnlyList<MoviePilotSubscriptionEpisode> Episodes)
{
    public int LibraryCount => Episodes.Count(episode => episode.InLibrary);
    public int MissingCount => Episodes.Count(episode => !episode.InLibrary);
    public string Summary => Episodes.Count == 0 ? "服务器尚未提供集信息，无法判断入库情况"
        : $"已入库 {LibraryCount} / {Episodes.Count} 项 · 未入库 {MissingCount} 项";

    public static MoviePilotSubscriptionFiles Parse(JsonElement data, MoviePilotSubscription subscription)
    {
        var current = MoviePilotSubscription.Parse(MoviePilotSubscription.Member(data, "subscribe"));
        if (current is null)
            throw new MoviePilotException("MoviePilot 暂未提供这条订阅的文件统计，请稍后刷新");
        if (current.Id != subscription.Id)
            throw new MoviePilotException("订阅已不存在或文件统计身份不匹配，请刷新列表");
        var episodes = MoviePilotSubscription.Member(data, "episodes");
        if (episodes.ValueKind != JsonValueKind.Object)
            throw new MoviePilotException("MoviePilot 未返回有效的文件统计");
        var result = new List<MoviePilotSubscriptionEpisode>();
        foreach (var entry in episodes.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, CultureInfo.InvariantCulture, out var number) || number < 0) continue;
            if (subscription.IsSeries && (number < current.StartEpisode || current.TotalEpisodes is > 0 && number > current.TotalEpisodes)) continue;
            if (!subscription.IsSeries && number != 0) continue;
            result.Add(new MoviePilotSubscriptionEpisode(number, MoviePilotSubscription.Text(entry.Value, "title"),
                MoviePilotSubscription.Text(entry.Value, "description"), Files(entry.Value, "library"), Files(entry.Value, "download"))
            { LibraryEntries = LibraryEntries(entry.Value), ArtworkUrl = MoviePilotSubscription.Text(entry.Value, "backdrop") });
        }
        // 元数据源可能少返回尚未播出的集；以订阅目标补齐，但不把“已下载”当作入库证据。
        var known = result.Select(episode => episode.Number).ToHashSet();
        if (subscription.IsSeries && current.TotalEpisodes is > 0 and <= 10000)
            for (var number = current.StartEpisode; number <= current.TotalEpisodes; number++)
                if (known.Add(number)) result.Add(new(number, "", "", [], []));
        return new(result.OrderBy(episode => episode.Number).ToArray());
    }

    private static IReadOnlyList<string> Files(JsonElement episode, string key)
    {
        var value = MoviePilotSubscription.Member(episode, key);
        if (value.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(value.GetString()) ? [] : [value.GetString()!];
        if (value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(file =>
        {
            var path = MoviePilotSubscription.Text(file, "file_path");
            var server = MoviePilotSubscription.Text(file, "server");
            var item = MoviePilotSubscription.Text(file, "itemid");
            return path.Length > 0 ? path : item.Length > 0 ? $"{server} · 媒体条目 {item}" : "";
        }).Where(file => file.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<MoviePilotSubscriptionLibraryFile> LibraryEntries(JsonElement episode)
    {
        var value = MoviePilotSubscription.Member(episode, "library");
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } path) return [new(path, "", "")];
        if (value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(file => new MoviePilotSubscriptionLibraryFile(
            MoviePilotSubscription.Text(file, "file_path"), MoviePilotSubscription.Text(file, "server_type"), MoviePilotSubscription.Text(file, "itemid"))).ToArray();
    }
}

/// <summary>
/// 只提交用户实际改动的可编辑字段，服务器维护的状态、入库事实及其他客户端的设置不回写。
/// <para>
/// <see cref="EpisodeGroup"/> 也在其中：订阅详情那一节的「切换剧集组」就是拿 <see cref="From"/> 的副本改这一个
/// 字段再提交（见 <c>MoviePilotSubscriptionsViewModel.SwitchEpisodeGroupAsync</c>）。**编辑弹窗必须把它原样带
/// 回来** —— 少了这一格，改别的字段会把订阅上已有的剧集组一起抹掉（空串在 <see cref="Changes"/> 里是「清空」）。
/// </para>
/// </summary>
public sealed record MoviePilotSubscriptionEdit(string Keyword, string Include, string Exclude,
    string Quality, string Resolution, string Effect, int TotalEpisodes, int StartEpisode, string EpisodeGroup)
{
    public static MoviePilotSubscriptionEdit From(MoviePilotSubscription subscription) => new(
        MoviePilotSubscription.Text(subscription.Data, "keyword"), MoviePilotSubscription.Text(subscription.Data, "include"),
        MoviePilotSubscription.Text(subscription.Data, "exclude"), MoviePilotSubscription.Text(subscription.Data, "quality"),
        MoviePilotSubscription.Text(subscription.Data, "resolution"), MoviePilotSubscription.Text(subscription.Data, "effect"),
        subscription.TotalEpisodes ?? 0, MoviePilotSubscription.Number(subscription.Data, "start_episode") ?? 0,
        subscription.EpisodeGroup);

    public IReadOnlyDictionary<string, object?> Changes(MoviePilotSubscription original)
    {
        var before = From(original);
        if (TotalEpisodes is < 0 or > 10000 || StartEpisode < 0 || TotalEpisodes > 0 && StartEpisode > TotalEpisodes)
            throw new MoviePilotException("请填写有效的起始集和总集数，起始集不能大于总集数");
        var result = new Dictionary<string, object?> { ["id"] = original.Id };
        Add("keyword", before.Keyword, Keyword); Add("include", before.Include, Include); Add("exclude", before.Exclude, Exclude);
        Add("quality", before.Quality, Quality); Add("resolution", before.Resolution, Resolution); Add("effect", before.Effect, Effect);
        if (original.IsSeries && before.TotalEpisodes != TotalEpisodes) result["total_episode"] = TotalEpisodes;
        if (original.IsSeries && before.StartEpisode != StartEpisode) result["start_episode"] = StartEpisode;
        // 剧集组是电视剧才有的一档；空串提交成 null，就是「改回 TMDB 默认排序」。
        if (original.IsSeries) Add("episode_group", before.EpisodeGroup, EpisodeGroup);
        return result;
        void Add(string name, string oldValue, string newValue)
        {
            if (oldValue != newValue.Trim()) result[name] = string.IsNullOrWhiteSpace(newValue) ? null : newValue.Trim();
        }
    }
}
