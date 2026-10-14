using System.Text.Json.Nodes;

namespace Momoka.Emby;

/// <summary>Jellyfin 管理页的已确认协议差异；隐藏选项不会删除服务器原文中的未知字段。</summary>
public static class JellyfinManagement
{
    public static bool SupportsUserPermission(string key) => key is not
        ("IsHiddenRemotely" or "IsHiddenFromUnusedDevices" or "EnableSubtitleDownloading"
        or "AllowCameraUpload" or "AllowSharingPersonalItems");

    public static bool SupportsLibrarySetting(string key) => key is
        "PreferredMetadataLanguage" or "MetadataCountryCode" or "EnablePhotos" or "EnableEmbeddedTitles"
        or "EnableRealtimeMonitor" or "EnableAutomaticSeriesGrouping" or "AutomaticRefreshIntervalDays"
        or "SaveLocalMetadata" or "Thumbnails" or "SubtitleDownloadLanguages" or "RequirePerfectSubtitleMatch"
        or "SkipSubtitlesIfAudioTrackMatches" or "SkipSubtitlesIfEmbeddedSubtitlesPresent"
        or "SaveSubtitlesWithMedia" or "SaveLyricsWithMedia";

    public static bool SupportsAdvancedSetting(string key) => key is
        "MetadataPath" or "PreferredMetadataLanguage" or "MetadataCountryCode" or "EnableExternalContentInSuggestions";

    public static JsonObject NewLibraryOptions(JsonObject available)
    {
        var options = new JsonObject { ["Enabled"] = true, ["EnableRealtimeMonitor"] = true };
        foreach (var item in (available["TypeOptions"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var target = EmbyLibraryOptions.TypeOptions(options, EmbyLibraryOptions.Text(item, "Type"));
            foreach (var key in new[] { "MetadataFetchers", "ImageFetchers" })
                target[key] = EmbyLibraryOptions.Array((item[key] as JsonArray ?? []).OfType<JsonObject>()
                    .Where(provider => EmbyLibraryOptions.Flag(provider, "DefaultEnabled"))
                    .Select(provider => EmbyLibraryOptions.Text(provider, "Name")));
        }
        return options;
    }
}
