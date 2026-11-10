using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Momoka.Danmaku;

public sealed partial class BilibiliClient
{
    private const int MaximumSearchResults = 1000;
    private const int MaximumSeasons = 20;
    private static readonly int[] WbiPermutation = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13];

    private async Task<DandanPlaySearchResult> SearchCoreAsync(string title, string? episode, LoginSession session, CancellationToken token)
    {
        title = title.Trim();
        var isLink = title.Contains("://", StringComparison.Ordinal);
        if (title.Length < 2 || title.Length > (isLink ? 2048 : 120)) throw new DandanPlayException("请输入作品名称或有效的 B站链接。");
        var episodeNumber = 0;
        if (!string.IsNullOrEmpty(episode) && (episode.Length > 6 || !episode.All(char.IsAsciiDigit)
            || !int.TryParse(episode, NumberStyles.None, CultureInfo.InvariantCulture, out episodeNumber) || episodeNumber <= 0))
            throw new DandanPlayException("集数必须是正整数，留空可搜索全部剧集。");
        var target = ParseTarget(title);
        using var navDocument = await ValidateIdentityAsync(session, token).ConfigureAwait(false);
        var cookie = session.Cookie;
        var results = new List<DandanPlayEpisode>();
        var ids = new HashSet<long>();
        if (target is { Kind: "bvid" or "aid" })
        {
            using var document = await GetJsonAsync("/x/web-interface/view?" + target.Kind + "=" + Uri.EscapeDataString(target.Id), cookie, token).ConfigureAwait(false);
            var data = Payload(document);
            if (!data.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
                throw new DandanPlayException("B站未返回可用的分P信息。");
            var animeTitle = CleanText(data, "title");
            foreach (var page in pages.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                var number = Number(page, "page");
                if ((target.Page > 0 && number != target.Page) || (episodeNumber > 0 && number != episodeNumber)) continue;
                AddResult(results, ids, Number(page, "cid"), animeTitle, "P" + number.ToString(CultureInfo.InvariantCulture) + " " + CleanText(page, "part"));
                if (results.Count == MaximumSearchResults) return new(results, true);
            }
            return new(results, false);
        }
        if (target is not null)
        {
            using var document = await GetJsonAsync("/pgc/view/web/season?" + target.Kind + "=" + target.Id, cookie, token).ConfigureAwait(false);
            var truncated = AppendSeason(Payload(document), results, ids, target.Kind == "ep_id" ? long.Parse(target.Id, CultureInfo.InvariantCulture) : 0, episodeNumber, token);
            return new(results, truncated);
        }

        // 只搜索番剧与影视，不把标题相近的UGC视频自动当成媒体库正片。
        // 上游 search.py 与 network.py 的固定提交见 docs/bilibili-source-reference.md。
        var mixinKey = WbiKey(Payload(navDocument));
        var seasons = new HashSet<long>();
        var hasMore = false;
        foreach (var type in new[] { "media_bangumi", "media_ft" })
        {
            var query = SignSearch(title, type, mixinKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            using var searchDocument = await GetJsonAsync("/x/web-interface/wbi/search/type?" + query, cookie, token).ConfigureAwait(false);
            var data = Payload(searchDocument);
            if (!data.TryGetProperty("result", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new DandanPlayException("B站返回了无法识别的搜索结果。");
            hasMore |= Number(data, "numPages") > 1 || Number(data, "numResults") > Number(data, "pagesize") && Number(data, "pagesize") > 0;
            foreach (var entry in entries.EnumerateArray())
            {
                var seasonId = Number(entry, "season_id");
                if (seasonId <= 0 || seasons.Contains(seasonId)) continue;
                if (seasons.Count == MaximumSeasons) { hasMore = true; continue; }
                seasons.Add(seasonId);
                using var seasonDocument = await GetJsonAsync("/pgc/view/web/season?season_id=" + seasonId.ToString(CultureInfo.InvariantCulture), cookie, token).ConfigureAwait(false);
                hasMore |= AppendSeason(Payload(seasonDocument), results, ids, 0, episodeNumber, token);
                if (results.Count == MaximumSearchResults) return new(results, true);
            }
        }
        return new(results, hasMore);
    }

    private static bool AppendSeason(JsonElement season, List<DandanPlayEpisode> result, HashSet<long> ids, long epId, int episodeNumber, CancellationToken token)
    {
        var title = CleanText(season, "title");
        var episodeLists = new List<JsonElement>();
        if (season.TryGetProperty("episodes", out var episodes) && episodes.ValueKind == JsonValueKind.Array) episodeLists.Add(episodes);
        if (season.TryGetProperty("section", out var sections) && sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
                if (section.ValueKind == JsonValueKind.Object && section.TryGetProperty("episodes", out var extra) && extra.ValueKind == JsonValueKind.Array) episodeLists.Add(extra);
        }
        if (episodeLists.Count == 0) throw new DandanPlayException("B站未返回可用的剧集信息。");
        foreach (var list in episodeLists)
        {
            foreach (var episode in list.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (epId > 0 && Number(episode, "id") != epId) continue;
                var episodeTitle = CleanText(episode, "title");
                if (episodeNumber > 0 && (!int.TryParse(episodeTitle, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number != episodeNumber)) continue;
                // 预告也有cid，但不能被集号过滤误当成正片。
                if (CleanText(episode, "badge") == "预告" && epId == 0) continue;
                AddResult(result, ids, Number(episode, "cid"), title, (episodeTitle + " " + CleanText(episode, "long_title")).Trim());
                if (result.Count == MaximumSearchResults) return true;
            }
        }
        return false;
    }

    private static void AddResult(List<DandanPlayEpisode> result, HashSet<long> ids, long cid, string title, string episodeTitle)
    {
        if (cid > 0 && title.Length > 0 && ids.Add(cid)) result.Add(new(cid, title, episodeTitle));
    }

    private static SearchTarget? ParseTarget(string input)
    {
        var page = 0;
        var wasUrl = false;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            wasUrl = true;
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0
                || !uri.IdnHost.Equals("www.bilibili.com", StringComparison.OrdinalIgnoreCase))
                throw new DandanPlayException("请使用 B站官方 BV、av、ep 或 ss 链接。");
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.StartsWith("/video/", StringComparison.Ordinal)) input = path[7..];
            else if (path.StartsWith("/bangumi/play/", StringComparison.Ordinal)) input = path[14..];
            else throw new DandanPlayException("请使用 B站官方 BV、av、ep 或 ss 链接。");
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!pair.StartsWith("p=", StringComparison.Ordinal)) continue;
                if (page > 0 || !int.TryParse(pair[2..], NumberStyles.None, CultureInfo.InvariantCulture, out page) || page <= 0)
                    throw new DandanPlayException("B站链接里的分P编号无效。");
            }
        }
        else if (input.Contains("://", StringComparison.Ordinal)) throw new DandanPlayException("B站链接无效。");
        if (BvidPattern().IsMatch(input)) return new("bvid", input, page);
        var match = NumericIdPattern().Match(input);
        if (match.Success && long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            return new(match.Groups[1].Value.ToLowerInvariant() switch { "av" => "aid", "ep" => "ep_id", _ => "season_id" }, id.ToString(CultureInfo.InvariantCulture), page);
        if (wasUrl || page > 0) throw new DandanPlayException("B站链接里的作品编号无效。");
        return null;
    }

    private static string WbiKey(JsonElement nav)
    {
        if (!nav.TryGetProperty("wbi_img", out var images) || images.ValueKind != JsonValueKind.Object)
            throw new DandanPlayException("B站未返回搜索验证信息，请稍后重试。");
        var original = ImageStem(CleanText(images, "img_url")) + ImageStem(CleanText(images, "sub_url"));
        if (original.Length != 64) throw new DandanPlayException("B站返回的搜索验证信息无效。");
        return new string(WbiPermutation.Select(index => original[index]).ToArray());
    }

    private static string ImageStem(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var filename = uri.Segments.Last();
        var stem = filename.Split('.')[0];
        return stem.Length == 32 && stem.All(char.IsAsciiLetterOrDigit) ? stem : "";
    }

    private static string SignSearch(string title, string type, string mixinKey, long timestamp)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = title,
            ["page"] = "1",
            ["page_size"] = "20",
            ["search_type"] = type,
            ["web_location"] = "1550101",
            ["wts"] = timestamp.ToString(CultureInfo.InvariantCulture),
        };
        var query = string.Join('&', parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value).Replace("%20", "+", StringComparison.Ordinal)));
        // WBI协议指定MD5，这里是公开参数签名而非密码保护。
        var signature = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey))).ToLowerInvariant();
        return query + "&w_rid=" + signature;
    }

    [GeneratedRegex("^BV1[A-Za-z0-9]{9}$", RegexOptions.CultureInvariant)]
    private static partial Regex BvidPattern();

    [GeneratedRegex("^(av|ep|ss)([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NumericIdPattern();

    private sealed record SearchTarget(string Kind, string Id, int Page);
}
