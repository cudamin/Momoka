using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotSubscriptionChoice(string Label, string Value)
{
    public override string ToString() => Value;
}

public sealed record MoviePilotSubscriptionEditorChoices(IReadOnlyList<MoviePilotSearchSite> Sites,
    IReadOnlyList<string> Downloaders, IReadOnlyList<MoviePilotSubscriptionChoice> Paths,
    IReadOnlyList<string> FilterGroups, IReadOnlyList<MoviePilotEpisodeGroup> EpisodeGroups, string Notice);

/// <summary>MoviePilot 网页端 constants.ts 的选项及请求值；已有自定义规则由编辑器原样保留。</summary>
public static class MoviePilotSubscriptionPresets
{
    public static IReadOnlyList<MoviePilotSubscriptionChoice> Qualities { get; } =
    [
        new("全部", ""), new("蓝光原盘", "Blu-?Ray.+VC-?1|Blu-?Ray.+AVC|UHD.+blu-?ray.+HEVC|MiniBD"),
        new("Remux", "Remux"), new("蓝光", "Blu-?Ray"), new("UHD", "UHD|UltraHD"),
        new("WEB-DL", "WEB-?DL|WEB-?RIP"), new("HDTV", "HDTV"), new("H.265", "[Hx].?265|HEVC"), new("H.264", "[Hx].?264|AVC")
    ];
    public static IReadOnlyList<MoviePilotSubscriptionChoice> Resolutions { get; } =
    [new("全部", ""), new("4K", "4K|2160p|x2160"), new("1080p", "1080[pi]|x1080"), new("720p", "720[pi]|x720")];
    public static IReadOnlyList<MoviePilotSubscriptionChoice> Effects { get; } =
    [
        new("全部", ""), new("杜比视界", @"Dolby[\s.]+Vision|DOVI|[\s.]+DV[\s.]+"),
        new("杜比全景声", @"Dolby[\s.]*\+?Atmos|Atmos"), new("HDR", @"[\s.]+HDR[\s.]+|HDR10|HDR10\+"), new("SDR", @"[\s.]+SDR[\s.]+")
    ];

    public static IReadOnlyList<MoviePilotSubscriptionChoice> DownloadPaths(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) throw new MoviePilotException("下载目录格式不正确");
        // 订阅端用 download_path，下载确认端另用 save_path；不可把存储前缀误写进订阅。
        return data.EnumerateArray().Select(item => new
        {
            Path = MoviePilotSubscription.Text(item, "download_path").Trim(),
            Name = MoviePilotSubscription.Text(item, "name").Trim()
        }).Where(item => item.Path.Length > 0).GroupBy(item => item.Path, StringComparer.Ordinal).Select(group =>
        {
            var names = group.Select(item => item.Name).Where(name => name.Length > 0 && name != group.Key).Distinct(StringComparer.Ordinal).ToArray();
            return new MoviePilotSubscriptionChoice(names.Length == 0 ? group.Key : $"{string.Join(" / ", names)} ({group.Key})", group.Key);
        }).ToArray();
    }
}

public sealed partial class MoviePilotService
{
    /// <summary>只读投影订阅编辑目录，不保存站点凭据；选项失败允许独立重试，已有草稿不重置。</summary>
    public async Task<MoviePilotSubscriptionEditorChoices> SubscriptionEditorChoicesAsync(MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var sites = Read("site/rss", "订阅站点", ParseSearchSites);
        var clients = Read("download/clients", "下载器", data => Values(data, "name"));
        var paths = Read("download/paths", "保存路径", MoviePilotSubscriptionPresets.DownloadPaths);
        var rules = Read("rule/groups?include_usage=false", "过滤规则组", data => Values(MoviePilotSubscription.Member(data, "rule_groups"), "name"));
        var groups = ReadGroups();
        await Task.WhenAll(sites, clients, paths, rules, groups).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        var a = await sites.ConfigureAwait(false); var b = await clients.ConfigureAwait(false);
        var c = await paths.ConfigureAwait(false); var d = await rules.ConfigureAwait(false); var e = await groups.ConfigureAwait(false);
        return new(a.Values, b.Values, c.Values, d.Values, e.Values,
            string.Join("、", new[] { a.Error, b.Error, c.Error, d.Error, e.Error }.Where(error => error.Length > 0)));

        async Task<(IReadOnlyList<T> Values, string Error)> Read<T>(string path, string label, Func<JsonElement, IReadOnlyList<T>> parse)
        {
            try
            {
                var data = await CallAsync((address, token, client) => client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
                return (parse(data), "");
            }
            catch (OperationCanceledException) { throw; }
            catch (MoviePilotException) { return ([], label); }
            catch (HttpRequestException) { return ([], label); }
        }

        async Task<(IReadOnlyList<MoviePilotEpisodeGroup> Values, string Error)> ReadGroups()
        {
            try { return (await SubscriptionEpisodeGroupsAsync(subscription, cancellationToken).ConfigureAwait(false), ""); }
            catch (OperationCanceledException) { throw; }
            catch (MoviePilotException) { return ([], "剧集组"); }
            catch (HttpRequestException) { return ([], "剧集组"); }
        }
    }
}
