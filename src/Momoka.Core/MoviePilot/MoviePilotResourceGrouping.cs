using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Momoka.MoviePilot;

/// <summary>只按完整发布名、体积和媒体身份归组；站点条目及下载身份始终保留。</summary>
public static class MoviePilotResourceGrouping
{
    public static IReadOnlyList<IReadOnlyList<MoviePilotResource>> Group(IEnumerable<MoviePilotResource> resources)
    {
        var groups = new List<IReadOnlyList<MoviePilotResource>>();
        var known = new Dictionary<string, List<MoviePilotResource>>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var title = string.Join(' ', resource.Title.Normalize(NormalizationForm.FormKC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
            var key = JsonSerializer.Serialize(new[] { title, resource.Size.ToString(CultureInfo.InvariantCulture),
                resource.MediaSource ?? "", resource.MediaId ?? "", resource.MediaType ?? "" });
            // 缺少体积不足以证明同一发布，不把同名但信息不全的资源折成一条。
            if (resource.Size > 0 && known.TryGetValue(key, out var group)) group.Add(resource);
            else
            {
                var created = new List<MoviePilotResource> { resource };
                groups.Add(created);
                if (resource.Size > 0) known[key] = created;
            }
        }
        return groups;
    }

    internal static IReadOnlyList<int> Numbers(JsonElement meta, string listName, string beginName, string endName)
    {
        if (meta.ValueKind != JsonValueKind.Object) return [];
        if (meta.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            var explicitNumbers = list.EnumerateArray().Select(Number).Where(value => value is >= 0 and <= 10000)
                .Select(value => value!.Value).Distinct().Order().ToArray();
            if (explicitNumbers.Length > 0) return explicitNumbers;
        }
        var begin = meta.TryGetProperty(beginName, out var start) ? Number(start) : null;
        var end = meta.TryGetProperty(endName, out var stop) ? Number(stop) : null;
        if (begin is not (>= 0 and <= 10000)) return [];
        if (end is null) return [begin.Value];
        return end >= begin && end <= 10000 ? Enumerable.Range(begin.Value, end.Value - begin.Value + 1).ToArray() : [];
    }

    private static int? Number(JsonElement value) => int.TryParse(value.ToString(), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var number) ? number : null;
}
