using System.Globalization;
using System.Text;
using System.Text.Json;
using Momoka.Emby;

namespace Momoka.Bangumi;

internal static class BangumiRatingIdentity
{
    private static bool IsProvider(string key) => key.Equals("Bangumi", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Bgm", StringComparison.OrdinalIgnoreCase);

    internal static bool HasProvider(EmbyItem item) => item.ProviderIds.Keys.Any(IsProvider);

    internal static long? For(EmbyItem item)
    {
        if (item.Type is not (EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season)) return null;

        // A season must carry its own subject ID. The parent and the display title cannot prove it.
        long? subject = null;
        foreach (var (key, value) in item.ProviderIds)
        {
            if (!IsProvider(key)) continue;
            if (!long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id is < 1 or > 10000000 || (subject is not null && subject != id)) return null;
            subject = id;
        }
        return subject;
    }

    internal static bool Matches(JsonElement candidate, EmbyItem item, int year)
    {
        if (candidate.ValueKind != JsonValueKind.Object || !candidate.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out var kind) || kind is not (2 or 6)
            || !DateOnly.TryParseExact(Text(candidate, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || date.Year != year) return false;
        // Fixed upstream pkg/vars/platform.go.json distinguishes animated/real movies from television.
        var platform = Text(candidate, "platform");
        var movie = platform is "剧场版" or "电影";
        var series = platform is "TV" or "WEB" or "OVA" or "日剧" or "欧美剧" or "华语剧" or "电视剧" or "综艺";
        if (item.Type == EmbyItemType.Movie ? !movie : item.Type != EmbyItemType.Series || !series) return false;
        var titles = new[] { item.Name, item.OriginalTitle }.Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => Normalize(title!)).Where(title => title.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (titles.Contains(Normalize(Text(candidate, "name")))
            || titles.Contains(Normalize(Text(candidate, "name_cn")))) return true;
        if (!candidate.TryGetProperty("infobox", out var infobox) || infobox.ValueKind != JsonValueKind.Array) return false;
        foreach (var field in infobox.EnumerateArray())
        {
            if (Text(field, "key") is not ("别名" or "中文名") || !field.TryGetProperty("value", out var value)) continue;
            if (value.ValueKind == JsonValueKind.String && titles.Contains(Normalize(value.GetString()!))) return true;
            if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray()
                .Any(alias => titles.Contains(Normalize(Text(alias, "v"))))) return true;
        }
        return false;
    }

    private static string Text(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static string Normalize(string title) => string.Concat(title.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
}
