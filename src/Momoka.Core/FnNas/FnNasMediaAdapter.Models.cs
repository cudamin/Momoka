using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnNas;

public sealed partial class FnNasMediaAdapter
{
    public static bool SupportsSort(string sort) => sort is EmbySortBy.Name or EmbySortBy.DateAdded or EmbySortBy.ReleaseDate or EmbySortBy.Rating;

    public static JsonObject Query(ItemQuery query)
    {
        if (query.Filters is { IsBlank: false } || !string.IsNullOrEmpty(query.PersonId)
            || !string.IsNullOrEmpty(query.NameStartsWith))
            throw new NotSupportedException("飞牛影视尚不支持此浏览条件");
        var sort = query.SortBy switch
        {
            EmbySortBy.Name => "sort_title",
            EmbySortBy.DateAdded => "create_time",
            EmbySortBy.ReleaseDate => "release_date",
            EmbySortBy.Rating => "vote_average",
            _ => throw new NotSupportedException("飞牛影视不支持此排序方式")
        };
        var types = query.IncludeItemTypes.Select(type => type switch
        {
            EmbyItemType.Series => "TV",
            EmbyItemType.Movie => "Movie",
            EmbyItemType.Video => "Video",
            EmbyItemType.Folder => "Directory",
            EmbyItemType.Season => "Season",
            EmbyItemType.Episode => "Episode",
            _ => ""
        }).Where(type => type.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var result = new JsonObject
        {
            ["page"] = Math.Max(0, query.StartIndex) / Math.Max(1, query.Limit) + 1,
            ["page_size"] = Math.Max(1, query.Limit),
            ["sort_column"] = sort,
            ["sort_type"] = query.Descending ? "DESC" : "ASC",
            ["exclude_grouped_video"] = 1,
            ["tags"] = new JsonObject
            {
                ["type"] = new JsonArray(types.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["genres"] = string.IsNullOrWhiteSpace(query.Genre) ? new JsonArray() : new JsonArray(query.Genre)
            }
        };
        if (!string.IsNullOrWhiteSpace(query.ParentId)) result["ancestor_guid"] = query.ParentId;
        if (query.IsPlayed is { } played) result["tags"]!["watched"] = played ? "1" : "0";
        return result;
    }

    public static EmbyItem Library(JsonObject value)
    {
        var item = Item(value);
        item.Name = Text(value, "name") is { Length: > 0 } name ? name : item.Name;
        item.Type = EmbyItemType.CollectionFolder;
        item.IsFolder = true;
        item.CollectionType = Text(value, "category") switch { "Movie" => "movies", "TV" => "tvshows", _ => "homevideos" };
        return item;
    }

    public static EmbyItem Item(JsonObject value)
    {
        var nativeType = Text(value, "type");
        var type = nativeType switch
        {
            "TV" => EmbyItemType.Series,
            "Season" => EmbyItemType.Season,
            "Episode" => EmbyItemType.Episode,
            "Movie" => EmbyItemType.Movie,
            "Video" => EmbyItemType.Video,
            "Directory" => EmbyItemType.Folder,
            _ => nativeType
        };
        var duration = Number(value, "duration");
        var position = Number(value, "ts") ?? Number(value, "watched_ts") ?? 0;
        var item = new EmbyItem
        {
            Id = Text(value, "guid"),
            Name = Text(value, "title"),
            Type = type,
            IsFolder = type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Folder,
            Overview = Text(value, "overview"),
            OriginalTitle = Text(value, "original_title"),
            PremiereDate = Date(value, "release_date"),
            DateCreated = Date(value, "create_time"),
            RunTimeTicks = duration is { } seconds ? Ticks(seconds) : null,
            CommunityRating = Number(value, "vote_average") is { } rating ? (float)rating : null,
            IndexNumber = Integer(value, type == EmbyItemType.Season ? "season_number" : "episode_number"),
            ParentIndexNumber = Integer(value, "season_number"),
            ParentId = Text(value, "parent_guid"),
            SeriesId = Text(value, "tv_guid"),
            SeriesName = Text(value, "tv_title"),
            SeasonId = type == EmbyItemType.Episode ? Text(value, "parent_guid") : null,
            ChildCount = Integer(value, type == EmbyItemType.Series ? "local_number_of_seasons" : "local_number_of_episodes"),
            Genres = Strings(value["genres"]).ToList(),
            UserData = new EmbyUserData
            {
                Played = Flag(value, "watched") || Flag(value, "is_watched"),
                IsFavorite = Flag(value, "is_favorite"),
                PlaybackPositionTicks = Ticks(position),
                PlayedPercentage = duration is > 0 ? position / duration * 100 : null
            }
        };
        item.ProductionYear = item.PremiereDate?.Year;
        var width = Number(value, "poster_width"); var height = Number(value, "poster_height");
        if (width is > 0 && height is > 0) item.PrimaryImageAspectRatio = width / height;
        var posters = Strings(value["posters"]).ToArray();
        var poster = Text(value, "poster");
        if (poster.Length == 0) poster = posters.FirstOrDefault() ?? "";
        var still = Text(value, "still_path");
        if (type == EmbyItemType.Episode && still.Length > 0) poster = still;
        if (poster.Length > 0) item.ImageTags["Primary"] = poster;
        if (still.Length > 0) item.ImageTags["Thumb"] = still;
        item.BackdropImageTags = Strings(value["backdrops"]).ToList();
        return item;
    }

    public static List<MediaSource> Sources(JsonObject value, string itemId, FnNasClient client, FnNasConnection connection)
    {
        var result = new List<MediaSource>();
        foreach (var file in Objects(value, "files"))
        {
            var id = Text(file, "guid");
            if (id.Length == 0) continue;
            var source = new MediaSource
            {
                Id = id,
                ItemId = itemId,
                Path = Text(file, "path"),
                Name = Path.GetFileName(Text(file, "path")),
                DirectStreamUrl = client.GetMediaUrl(connection, id),
                SupportsDirectPlay = true,
                SupportsDirectStream = true
            };
            source.Container = Path.GetExtension(source.Path)?.TrimStart('.');
            AddStreams(source, value, "video_streams", "Video");
            AddStreams(source, value, "audio_streams", "Audio");
            AddStreams(source, value, "subtitle_streams", "Subtitle");
            source.DefaultAudioStreamIndex = source.MediaStreams.FirstOrDefault(stream => stream.IsAudio && stream.IsDefault)?.Index;
            source.DefaultSubtitleStreamIndex = source.MediaStreams.FirstOrDefault(stream => stream.IsSubtitle && stream.IsDefault)?.Index;
            result.Add(source);
        }
        return result;
    }

    private static void AddStreams(MediaSource source, JsonObject value, string key, string type)
    {
        foreach (var stream in Objects(value, key).Where(stream => Text(stream, "media_guid") == source.Id))
        {
            // An external subtitle requires a separate, verified download contract. Do not invent a
            // container index or silently request an Emby subtitle URL for a FnNas sidecar.
            if (Flag(stream, "is_external")) continue;
            var index = Integer(stream, "index");
            if (index is null && type != "Video") continue;
            source.MediaStreams.Add(new MediaStream
            {
                Index = index ?? 0,
                ProviderId = Text(stream, "guid"),
                Type = type,
                Codec = Text(stream, "codec_name"),
                Profile = Text(stream, "profile"),
                Language = Text(stream, "language"),
                Title = Text(stream, "title"),
                ChannelLayout = Text(stream, "channel_layout"),
                Channels = Integer(stream, "channels"),
                Width = Integer(stream, "width"),
                Height = Integer(stream, "height"),
                BitRate = Integer(stream, "bps"),
                BitDepth = Integer(stream, "bit_depth"),
                RealFrameRate = FrameRate(Text(stream, "r_frame_rate")),
                VideoRange = Text(stream, "color_range_type"),
                AspectRatio = Text(stream, "display_aspect_ratio"),
                IsDefault = Flag(stream, "is_default"),
                IsForced = Flag(stream, "forced")
            });
        }
    }

    public static JsonObject PlaybackRecord(PlaybackReport report) => new()
    {
        ["item_guid"] = report.ItemId,
        ["media_guid"] = report.MediaSourceId,
        ["video_guid"] = report.FnNasSelection?.VideoGuid ?? "",
        ["audio_guid"] = report.FnNasSelection?.AudioGuid(report.AudioStreamIndex) ?? "",
        ["subtitle_guid"] = report.FnNasSelection?.SubtitleGuid(report.SubtitleStreamIndex) ?? "",
        ["resolution"] = "original",
        ["bitrate"] = -1,
        ["duration"] = (report.FnNasSelection?.DurationTicks ?? 0) / (double)TimeSpan.TicksPerSecond,
        ["ts"] = Math.Max(0, report.PositionTicks) / (double)TimeSpan.TicksPerSecond
    };

    private static double? FrameRate(string value)
    {
        var parts = value.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)) return null;
        if (parts.Length == 1) return double.IsFinite(numerator) && numerator >= 0 ? numerator : null;
        return parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0 ? numerator / denominator : null;
    }

    private static DateTimeOffset? Date(JsonObject value, string key)
    {
        if (Number(value, key) is > 0 and < 253402300799 and var timestamp) return DateTimeOffset.FromUnixTimeSeconds((long)timestamp);
        return DateTimeOffset.TryParse(Text(value, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }

    private static IEnumerable<string> Strings(JsonNode? node)
    {
        if (node is JsonArray array) return array.Select(value => value?.ToString() ?? "").Where(value => value.Length > 0);
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            if (JsonNode.Parse(text) is JsonArray parsed) return Strings(parsed).ToArray();
        }
        catch (System.Text.Json.JsonException) { }
        return [text];
    }
}
