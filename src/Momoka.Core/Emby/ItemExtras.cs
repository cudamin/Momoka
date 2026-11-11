namespace Momoka.Emby;

/// <summary>两家服务器的附加内容前提与媒体过滤；不改写响应的媒体身份和用户状态。</summary>
public static class ItemExtras
{
    public static bool CanRead(EmbyItem item, MediaServerKind kind) => kind switch
    {
        // Emby 4.10.1 itemhelper.supportsExtras checks MediaType rather than SpecialFeatureCount.
        MediaServerKind.Emby => !item.IsFolder && item.Type is not "TvChannel" and not "Program"
            && item.MediaType == "Video",
        MediaServerKind.Jellyfin => item.SpecialFeatureCount > 0,
        _ => false
    };

    public static bool IsPlayable(EmbyItem item) => IsItemId(item.Id) && item.IsPlayable
        && item.Type != EmbyItemType.Audio && !item.IsFolder && !item.IsDisplayOnly
        && (item.MediaType is null or "Video")
        && !string.Equals(item.LocationType, "Virtual", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(item.LocationType, "Offline", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(item.PlayAccess, "None", StringComparison.OrdinalIgnoreCase);

    public static bool IsItemId(string? id) => !string.IsNullOrWhiteSpace(id) && id is not "." and not ".."
        && !id.Any(character => char.IsControl(character) || character is '/' or '\\' or '?' or '#');

    public static string Category(string? type) => type switch
    {
        "BehindTheScenes" => "幕后花絮",
        "DeletedScene" => "删减片段",
        "Interview" => "访谈",
        "Featurette" => "制作特辑",
        "Short" => "短片",
        "Scene" => "场景片段",
        "Sample" => "试看片段",
        "Clip" => "片段",
        "Trailer" => "预告片",
        _ => "特典"
    };
}
