namespace Momoka.Emby;

/// <summary>Jellyfin binds these query values to enums; Emby's additional field names cause HTTP 400.</summary>
public static class JellyfinQuery
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "AirTime", "CanDelete", "CanDownload", "ChannelInfo", "Chapters", "Trickplay", "ChildCount",
        "CumulativeRunTimeTicks", "CustomRating", "DateCreated", "DateLastMediaAdded", "DisplayPreferencesId",
        "Etag", "ExternalUrls", "Genres", "ItemCounts", "MediaSourceCount", "MediaSources", "OriginalTitle",
        "Overview", "ParentId", "Path", "People", "PlayAccess", "ProductionLocations", "ProviderIds",
        "PrimaryImageAspectRatio", "RecursiveItemCount", "Settings", "SeriesStudio", "SortName",
        "SpecialEpisodeNumbers", "Studios", "Taglines", "Tags", "RemoteTrailers", "MediaStreams",
        "SeasonUserData", "DateLastRefreshed", "DateLastSaved", "RefreshState", "ChannelImage",
        "EnableMediaSourceDisplay", "Width", "Height", "ExtraIds", "LocalTrailerCount", "IsHD", "SpecialFeatureCount"
    };

    private static readonly HashSet<string> Sorts = new(StringComparer.Ordinal)
    {
        "Default", "AiredEpisodeOrder", "Album", "AlbumArtist", "Artist", "DateCreated", "OfficialRating",
        "DatePlayed", "PremiereDate", "StartDate", "SortName", "Name", "Random", "Runtime", "CommunityRating",
        "ProductionYear", "PlayCount", "CriticRating", "IsFolder", "IsUnplayed", "IsPlayed", "SeriesSortName",
        "VideoBitRate", "AirTime", "Studio", "IsFavoriteOrLiked", "DateLastContentAdded", "SeriesDatePlayed",
        "ParentIndexNumber", "IndexNumber"
    };

    public static string FilterFields(string fields) => string.Join(',', fields.Split(',').Where(Fields.Contains));
    public static bool SupportsSort(string sort) => sort.Split(',').All(Sorts.Contains);
    public static string FilterSort(string sort) => SupportsSort(sort) ? sort : EmbySortBy.Name;
}
