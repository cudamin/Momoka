using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Momoka.MoviePilot;

/// <summary>当前订阅的游标分页；无记录与读取失败分别处理，不能由日志补造历史。</summary>
public sealed record MoviePilotSubscriptionActivityPage(IReadOnlyList<MoviePilotSubscriptionActivity> Items, string? NextCursor)
{
    public static MoviePilotSubscriptionActivityPage Parse(JsonElement data)
    {
        var items = MoviePilotSubscription.Member(data, "items");
        var cursor = MoviePilotSubscription.Member(data, "next_cursor");
        if (items.ValueKind != JsonValueKind.Array || cursor.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            throw new MoviePilotException("MoviePilot 活动列表格式不正确，请重试");
        var next = cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
        if (next is not null && !MoviePilotSubscriptionActivity.ValidIdentifier(next))
            throw new MoviePilotException("MoviePilot 活动分页标识无效，请重新读取");
        return new(items.EnumerateArray().Select(MoviePilotSubscriptionActivity.Parse).DistinctBy(item => item.EventId).ToArray(), next);
    }
}

/// <summary>仅保留可显示的活动投影；资源原对象、凭据与下载地址不向 UI 暴露。</summary>
public sealed partial record MoviePilotSubscriptionActivity(string EventId, string Kind, string Source, string Status,
    string Title, string Summary, DateTimeOffset? StartedAt, string Details, bool DetailsLoaded)
{
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["include"] = "包含",
        ["exclude"] = "排除",
        ["quality"] = "质量",
        ["resolution"] = "分辨率",
        ["effect"] = "特效",
        ["sites"] = "站点",
        ["filter_groups"] = "过滤规则组",
        ["best_version"] = "洗版",
        ["best_version_full"] = "仅洗全集",
        ["start_episode"] = "开始集数",
        ["total_episode"] = "总集数",
        ["custom_words"] = "识别词",
        ["keyword"] = "搜索关键词",
        ["search_interval"] = "搜索周期",
        ["state"] = "订阅状态",
        ["episode_group"] = "剧集组"
    };

    internal static bool ValidIdentifier(string value) => value.Length is >= 1 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    public static MoviePilotSubscriptionActivity Parse(JsonElement data)
    {
        var id = String(data, "event_id");
        var kind = String(data, "kind");
        var status = String(data, "status");
        var counts = MoviePilotSubscription.Member(data, "counts");
        var details = MoviePilotSubscription.Member(data, "details");
        if (!ValidIdentifier(id) || kind.Length == 0 || status.Length == 0 || counts.ValueKind != JsonValueKind.Object
            || details.ValueKind != JsonValueKind.Object)
            throw new MoviePilotException("MoviePilot 活动记录格式不正确，请重试");
        var title = kind switch
        {
            "created" => "新建订阅",
            "updated" => "更新订阅规则",
            "status" => "订阅状态变更",
            "completed" => "订阅完成",
            "refresh" => "刷新匹配",
            "search" => "订阅搜索",
            _ => "订阅活动"
        };
        var phase = String(details, "phase");
        var result = phase == "queued" && status == "waiting" ? "已安排搜索" : status switch
        {
            "waiting" => "等待继续",
            "running" => "正在处理",
            "failed" => "处理失败",
            "cancelled" => "已停止",
            "completed" => "处理完成",
            "empty" => "无可下载资源",
            "not_submitted" => "未提交新下载",
            _ => "状态未知"
        };
        var summary = result;
        if (phase != "queued" && kind == "refresh")
            summary += $" · {Count(counts, "discovered")} 项共享候选 · 本订阅相关 {Count(counts, "related")} 项 · 提交下载 {Count(counts, "submitted")} 项";
        else if (phase != "queued" && kind == "search")
            summary += $" · 已请求 {Count(counts, "requested_sites")} / {Count(counts, "sites")} 个站点 · 发现 {Count(counts, "discovered")} 项 · 符合 {Count(counts, "qualified")} 项 · 提交下载 {Count(counts, "submitted")} 项";
        var started = DateTimeOffset.TryParse(String(data, "started_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? (DateTimeOffset?)parsed : null;
        var loaded = MoviePilotSubscription.Member(data, "details_loaded").ValueKind != JsonValueKind.False;
        var source = String(data, "source") switch
        {
            "manual" or "user" or "targeted" => "用户触发",
            "automatic" or "system" or "scheduled" or "rss" => "自动触发",
            _ => "来源未知"
        };
        return new(id, kind, source, status, title, summary, started, Describe(details), loaded);
    }

    private static string Describe(JsonElement details)
    {
        var lines = new List<string>();
        Add(String(details, "reason"));
        if (DateTimeOffset.TryParse(String(details, "retry_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var retry))
            Add("计划继续：" + retry.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        var changes = MoviePilotSubscription.Member(details, "changes");
        if (changes.ValueKind == JsonValueKind.Array)
            foreach (var change in changes.EnumerateArray().Take(100))
                if (Fields.TryGetValue(String(change, "field"), out var label))
                    Add(label + "：" + Value(MoviePilotSubscription.Member(change, "before")) + " → " + Value(MoviePilotSubscription.Member(change, "after")));
        var rules = MoviePilotSubscription.Member(details, "rules");
        if (rules.ValueKind == JsonValueKind.Object)
        {
            foreach (var (field, label) in Fields)
            {
                var value = MoviePilotSubscription.Member(rules, field);
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                var text = Value(value);
                if (text.Length > 0 && text != "未设置") Add(label + "：" + text);
            }
        }
        var resources = MoviePilotSubscription.Member(details, "resources");
        if (resources.ValueKind == JsonValueKind.Array)
            foreach (var resource in resources.EnumerateArray().Take(100))
            {
                var title = String(resource, "title");
                if (title.Length == 0) continue;
                var decision = String(resource, "decision") switch
                {
                    "submitted" => "已提交下载",
                    "qualified" => "符合条件，未提交下载",
                    "rejected" => "未满足下载条件",
                    "failed" => "下载提交失败",
                    "related" => "已关联，等待判断",
                    _ => "处理结果未知"
                };
                var site = String(resource, "site");
                var reason = String(resource, "reason");
                Add(title + (site.Length > 0 ? " · " + site : "") + " · " + decision + (reason.Length > 0 ? " · " + reason : ""));
            }
        if (MoviePilotSubscription.Member(details, "resources_omitted") is { ValueKind: JsonValueKind.Number } omitted
            && omitted.TryGetInt32(out var omittedCount) && omittedCount > 0)
            Add($"另有 {omittedCount} 项资源未保留明细，概要计数包含全部资源。");
        var sites = MoviePilotSubscription.Member(details, "sites");
        if (sites.ValueKind == JsonValueKind.Array)
            foreach (var site in sites.EnumerateArray().Take(100))
            {
                var name = String(site, "name");
                if (name.Length == 0) continue;
                var state = String(site, "status") switch
                {
                    "checked" => "已检查候选",
                    "success" => "请求成功",
                    "completed" => "已完成",
                    "failed" or "error" => "请求失败",
                    "waiting" or "deferred" => "等待继续",
                    "skipped" => "未请求",
                    "received" => "已收到候选",
                    _ => "状态未知"
                };
                Add(name + " · 发现 " + Count(site, "candidates") + " 项 · " + state);
            }
        var attempts = MoviePilotSubscription.Member(details, "attempts");
        if (attempts.ValueKind == JsonValueKind.Array)
            foreach (var attempt in attempts.EnumerateArray().Take(100))
            {
                var attemptCounts = MoviePilotSubscription.Member(attempt, "counts");
                var state = String(attempt, "status") switch
                {
                    "waiting" => "等待继续",
                    "running" => "正在处理",
                    "failed" => "处理失败",
                    "cancelled" => "已停止",
                    "completed" => "处理完成",
                    "empty" => "无可下载资源",
                    _ => "状态未知"
                };
                Add("恢复执行 · " + state + " · 相关资源 " + Count(attemptCounts, "related") + " 项 · 提交下载 " + Count(attemptCounts, "submitted") + " 项");
                Add(Describe(attempt));
            }
        return string.Join('\n', lines);

        void Add(string line) { if (!string.IsNullOrWhiteSpace(line)) lines.Add(line); }
    }

    private static string String(JsonElement data, string key) => Safe(MoviePilotSubscription.Member(data, key));
    private static string Count(JsonElement data, string key) => MoviePilotSubscription.Member(data, key) is { ValueKind: JsonValueKind.Number } number
        && number.TryGetInt32(out var count) && count >= 0 ? count.ToString(CultureInfo.InvariantCulture) : "未知";
    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "未设置",
        JsonValueKind.String => Safe(value),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "开启",
        JsonValueKind.False => "关闭",
        JsonValueKind.Array => string.Join("、", value.EnumerateArray().Take(100).Select(Value).Where(item => item.Length > 0)),
        _ => ""
    };
    private static string Safe(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        text = Link().Replace(text, "[链接已隐藏]");
        text = Credential().Replace(text, "[敏感内容已隐藏]");
        text = new string(text.Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
        return text.Length > 2048 ? text[..2048] + "…" : text;
    }

    [GeneratedRegex(@"https?://[^\s<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Link();
    [GeneratedRegex(@"(?:authorization\s*[:=]\s*|bearer\s+|(?:cookie|password|access_token|api_token|token)\s*[:=]\s*)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credential();
}
