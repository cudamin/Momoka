using System.Globalization;
using System.Text.Json.Nodes;

namespace Momoka.Plex;

public sealed class PlexFilterSelection
{
    public string Type { get; set; } = "";
    /// <summary>Null preserves pre-existing explicit selections; false marks an automatic library default.</summary>
    public bool? TypeIsExplicit { get; set; }
    public Dictionary<string, List<string>> Values { get; set; } = new(StringComparer.Ordinal);
    public bool HasConditions => Type.Length > 0 && TypeIsExplicit != false || Count > 0;
    public bool IsBlank => Type.Length == 0 && Values.Values.All(values => values.Count == 0);
    public int Count => Values.Count(pair => pair.Value.Count > 0);
    public PlexFilterSelection Clone() => new() { Type = Type, TypeIsExplicit = TypeIsExplicit,
        Values = Values.ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value), StringComparer.Ordinal) };
    public void Clear() { Values.Clear(); }
}

public sealed record PlexFilterOption(string Value, string Title);
public sealed record PlexLibraryFilter(string Field, string Title, string Kind, string Endpoint);
public sealed record PlexLibrarySort(string Key, string DescKey, string Title, bool DefaultDescending, string FirstCharacterKey)
{
    public string QueryKey(bool descending) => descending
        ? DescKey.Length > 0 ? DescKey : throw new InvalidOperationException("服务器没有提供此排序的降序，请重新选择排序")
        : Key;
}
public sealed record PlexLibraryType(string Id, string Title, IReadOnlyList<PlexLibraryFilter> Filters, IReadOnlyList<PlexLibrarySort> Sorts);
public sealed record PlexFirstCharacter(string Key, string Title, int Size, int Index);

/// <summary>PMS-declared fields and sorts for exactly one section. Labels never become identifiers.</summary>
public sealed record PlexLibraryCatalog(string SectionId, IReadOnlyList<PlexLibraryType> Types)
{
    public PlexLibraryType RequireType(string id) => Types.FirstOrDefault(type => type.Id == id)
        ?? throw new InvalidOperationException("保存的媒体类型已不在服务器目录中，请重试或清除筛选");

    public static PlexLibraryCatalog Parse(string section, JsonObject value, bool filtersEnabled)
    {
        PlexEndpoint.Segment(section);
        var types = new List<PlexLibraryType>();
        foreach (var item in Elements(value["Type"]))
        {
            var native = Text(item, "type");
            var id = native switch { "movie" => "1", "show" => "2", "season" => "3", "episode" => "4", "clip" => "12", _ => "" };
            if (id.Length == 0 || !ValidTypePath(Text(item, "key"), section, id)) continue;
            var fields = new List<PlexLibraryFilter>();
            if (filtersEnabled)
                foreach (var filter in Elements(item["Filter"]))
                {
                    var field = Text(filter, "filter");
                    var kind = Text(filter, "filterType");
                    if (!SupportedField(field, native) || kind is not ("boolean" or "string" or "integer")) continue;
                    var endpoint = FieldEndpoint(field);
                    if (kind != "boolean" && !ValidFieldPath(Text(filter, "key"), section, id, endpoint)) continue;
                    fields.Add(new(field, Label(field, Text(filter, "title")), kind, endpoint));
                }
            var sorts = Elements(item["Sort"]).Select(sort => new PlexLibrarySort(Text(sort, "key"), Text(sort, "descKey"),
                SortLabel(Text(sort, "key"), Text(sort, "title")), Text(sort, "defaultDirection") == "desc" || Text(sort, "default") == "desc",
                Text(sort, "firstCharacterKey"))).Where(sort => SafeSort(sort.Key) && (sort.DescKey.Length == 0 || SafeSort(sort.DescKey)))
                .DistinctBy(sort => sort.Key).ToArray();
            types.Add(new(id, TypeLabel(native, Text(item, "title")), fields.DistinctBy(filter => filter.Field).ToArray(), sorts));
        }
        return new(section, types.DistinctBy(type => type.Id).ToArray());
    }

    public static IReadOnlyList<PlexFilterOption> Options(JsonObject value, PlexLibraryFilter filter) =>
        Elements(value["Directory"]).Select(item => new PlexFilterOption(
            item.ContainsKey("field") ? Text(item, "value") : Text(item, "key"), Text(item, "title")))
            .Where(option => option.Value.Length > 0 && option.Title.Length > 0 && !option.Value.Any(char.IsControl))
            .DistinctBy(option => option.Value).ToArray();

    public static IReadOnlyList<PlexFirstCharacter> FirstCharacters(JsonObject value)
    {
        var result = new List<PlexFirstCharacter>();
        long index = 0;
        foreach (var item in Elements(value["Directory"]))
        {
            var key = Text(item, "key");
            if (key.Length == 0 || !int.TryParse(Text(item, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size <= 0) continue;
            if (index + size > int.MaxValue) throw new InvalidDataException("Plex 字母索引数量超出可用范围");
            result.Add(new(key, Text(item, "title") is { Length: > 0 } title ? title : key, size, (int)index));
            index += size;
        }
        return result;
    }

    public static bool SafeSort(string value) => value.Length > 0 && value.Split(',').All(part =>
    {
        var pair = part.Split(':');
        return pair.Length is 1 or 2 && pair[0].Length > 0 && pair[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_')
            && (pair.Length == 1 || pair[1] is "asc" or "desc");
    });

    private static bool SupportedField(string field, string native)
    {
        var parts = field.Split('.');
        return parts.Length is 1 or 2 && (parts.Length == 1 || parts[0] == native)
            && parts[^1] is "genre" or "year" or "unwatched" or "unwatchedLeaves" or "resolution";
    }
    private static string FieldEndpoint(string field) => field.Split('.')[^1];
    private static bool ValidTypePath(string key, string section, string type) => ValidPath(key, section, type, "all");
    private static bool ValidFieldPath(string key, string section, string type, string field) => ValidPath(key, section, type, field);
    private static bool ValidPath(string key, string section, string type, string suffix)
    {
        if (key.Contains('#') || key.Contains('\\') || key.Any(char.IsControl)) return false;
        var parts = key.Split('?', 2);
        if (parts[0] != "/library/sections/" + section + "/" + suffix) return false;
        return parts.Length == 1 || parts[1].Split('&').All(pair => pair == "type=" + type);
    }
    private static string Label(string field, string original) => field.Split('.')[^1] switch
    { "genre" => "类型", "year" => "年份", "unwatched" or "unwatchedLeaves" => "已看状态", "resolution" => "分辨率", _ => original };
    private static string TypeLabel(string native, string original) => native switch
    { "movie" => "电影", "show" => "电视剧", "season" => "季", "episode" => "单集", "clip" => "视频", _ => original };
    private static string SortLabel(string key, string original) => key switch
    { "titleSort" => "标题", "addedAt" => "添加时间", "originallyAvailableAt" => "上映时间", "rating" => "评分", "year" => "年份", "duration" => "时长", "viewCount" => "播放次数", "lastViewedAt" => "最近观看", "random" => "随机", _ => original.Length > 0 ? original : key };
    internal static string Text(JsonObject value, string key) => value[key]?.ToString() ?? "";
    internal static IEnumerable<JsonObject> Elements(JsonNode? value) => value switch
    { JsonArray array => array.OfType<JsonObject>(), JsonObject single => [single], _ => [] };
}
