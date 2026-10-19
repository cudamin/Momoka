using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>
/// 飞牛「更多」菜单里那几条写操作的请求正文。官方前端 0.9.8 的字段名与取值来自
/// <c>item/refresh</c>、<c>scrap/search</c>、<c>scrap/rescrap</c>、<c>scrap/:guid</c> 与
/// <c>item/:guid</c> 的调用处；拼正文放在 Core，好让每条请求都有离线测试。
/// <para>
/// 这些是服务器的「命令」而不是本客户端的抽象。字段名写错时服务器常常仍回 200 加一个
/// 业务错误码，屏上只看见「失败」—— 所以宁可在这里一条条钉住。
/// </para>
/// </summary>
public static class FnNasItemActions
{
    public const string SearchByTitle = "title";
    public const string SearchByImdbId = "imdb_id";
    public const string SearchByTmdbId = "tmdb_id";

    /// <summary>刷新模式：官方前端是数字，0 覆盖全部、1 只补缺失。</summary>
    public const int RefreshAll = 0;
    public const int RefreshMissing = 1;

    public static int RefreshMode(string mode) => mode == "all" ? RefreshAll : RefreshMissing;

    public static JsonObject SearchScrap(string itemGuid, string source, string keyword, string language) => new()
    {
        ["item_guid"] = itemGuid,
        ["source"] = source,
        ["source_keyword"] = keyword.Trim(),
        ["lan"] = language
    };

    public static JsonObject Refresh(string itemGuid, int mode) => new()
    {
        ["item_guid"] = itemGuid,
        ["refresh_mode"] = mode
    };

    /// <summary>
    /// 删除条目（或它的某个版本）。官方前端在「管理版本」里删一个文件时仍走同一条
    /// <c>DELETE item/:guid</c>，只是把 <c>media_guids</c> 点名。
    /// </summary>
    public static JsonObject Delete(bool deleteFile, IEnumerable<string> mediaGuids)
    {
        var array = new JsonArray();
        foreach (var guid in mediaGuids.Where(guid => guid.Length > 0).Distinct(StringComparer.Ordinal))
            array.Add(guid);

        return new JsonObject
        {
            ["delete_file"] = deleteFile ? 1 : 0,
            ["media_guids"] = array
        };
    }

    /// <summary>
    /// 手动匹配提交的那一份。剧／季是「一叠文件」，官方前端给它 <c>media_guids: []</c>；
    /// 电影和单集点名自己那几个文件。季集号只在剧集类型上出现，方式和官方前端一致。
    /// </summary>
    public static JsonObject Rescrap(EmbyItem item, FnNasScrapResult result, IReadOnlyList<FnNasVersion> files)
    {
        var body = new JsonObject
        {
            ["item_guid"] = item.Id,
            ["source"] = result.Source,
            ["source_id"] = result.SourceId,
            ["type"] = result.Type.Length > 0 ? result.Type : DefaultType(item),
            ["media_guids"] = MediaGuids(item, files)
        };

        if (item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode)
        {
            body["season_number"] = item.Type == EmbyItemType.Episode
                ? item.ParentIndexNumber ?? 0
                : item.Type == EmbyItemType.Season ? item.IndexNumber ?? 0 : 0;
            body["episode_number"] = item.Type == EmbyItemType.Episode ? item.IndexNumber ?? 0 : 0;
            body["scrap_tv_type"] = item.Type switch
            {
                EmbyItemType.Series => "MultiSeasonMultiEpisode",
                EmbyItemType.Season => "MultiEpisode",
                _ => "SingleEpisode"
            };
        }

        return body;
    }

    private static string DefaultType(EmbyItem item) => item.Type switch
    {
        EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode => "TV",
        _ => "Movie"
    };

    private static JsonArray MediaGuids(EmbyItem item, IReadOnlyList<FnNasVersion> files)
    {
        // 剧和季是一叠文件：服务器的「整理整份文件夹」路径认空清单，逐集点名会把整部剧拆成几十次匹配。
        if (item.Type is EmbyItemType.Series or EmbyItemType.Season) return new JsonArray();

        var array = new JsonArray();
        foreach (var guid in files.Select(file => file.MediaGuid).Where(guid => guid.Length > 0).Distinct(StringComparer.Ordinal))
            array.Add(guid);
        return array;
    }
}

/// <summary>`scrap/search` 返回的一条候选。只读我们提交时要带回去的字段。</summary>
public sealed record FnNasScrapResult(string Title, string Source, string SourceId, string Type, string ReleaseDate)
{
    public static FnNasScrapResult From(JsonObject value) => new(
        Text(value, "title") is { Length: > 0 } title ? title : Text(value, "name"),
        Text(value, "source"),
        Text(value, "source_id"),
        Text(value, "type"),
        Text(value, "release_date") is { Length: > 0 } release ? release : Text(value, "first_air_date"));

    private static string Text(JsonObject value, string key) => value[key] switch
    {
        JsonValue scalar => scalar.ToString(),
        _ => ""
    };
}

/// <summary>`media/itemfile/:guid` 的一个文件，也就是「版本」表上的一行。</summary>
public sealed record FnNasVersion(string MediaGuid, string Path, long? Size, string Resolution)
{
    public static FnNasVersion From(JsonObject value) => new(
        Text(value, "media_guid"),
        Text(value, "file_path") is { Length: > 0 } path ? path : Text(value, "path"),
        Long(value, "file_size") ?? Long(value, "size"),
        Text(value, "resolution") is { Length: > 0 } resolution ? resolution : Height(value));

    /// <summary>版本表第二行：清晰度、体积，有什么说什么。</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(2);
            if (Resolution.Length > 0) parts.Add(Resolution);
            if (Size is > 0) parts.Add(SizeText(Size.Value));
            return string.Join(" · ", parts);
        }
    }

    private static string Text(JsonObject value, string key) => value[key] switch
    {
        JsonValue scalar => scalar.ToString(),
        _ => ""
    };

    private static long? Long(JsonObject value, string key) =>
        value[key] is JsonValue scalar && long.TryParse(scalar.ToString(), out var number) ? number : null;

    /// <summary>服务器有时只给高度而没有 <c>resolution</c>。</summary>
    private static string Height(JsonObject value) => Long(value, "height") switch
    {
        >= 2000 => "4K",
        >= 1000 => "1080p",
        >= 700 => "720p",
        _ => ""
    };

    private static string SizeText(long bytes)
    {
        var gib = bytes / 1024d / 1024 / 1024;
        return gib >= 1 ? $"{gib:0.##} GB" : $"{bytes / 1024d / 1024:0.#} MB";
    }
}
