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
            || !string.IsNullOrEmpty(query.NameStartsWith) || !string.IsNullOrWhiteSpace(query.Genre))
            throw new NotSupportedException("飞牛影视尚不支持此浏览条件");
        var sort = query.SortBy switch
        {
            EmbySortBy.Name => "sort_title",
            EmbySortBy.DateAdded => "create_time",
            EmbySortBy.ReleaseDate => "release_date",
            EmbySortBy.Rating => "vote_average",
            _ => throw new NotSupportedException("飞牛影视不支持此排序方式")
        };
        var selection = query.FnNasFilters;
        // 筛选面板点的那一类优先于这一页自身的类型；没有点就仍按这一页装的是什么问。
        var types = selection is { Type.Length: > 0 } chosen ? [chosen.Type]
            : query.IncludeItemTypes.Select(type => type switch
            {
                EmbyItemType.Series => "TV",
                EmbyItemType.Movie => "Movie",
                EmbyItemType.Video => "Video",
                EmbyItemType.Folder => "Directory",
                EmbyItemType.Season => "Season",
                EmbyItemType.Episode => "Episode",
                _ => ""
            }).Where(type => type.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var tags = new JsonObject();
        // The server answers -4 (InternalError) for an empty array under tags; a missing key means
        // "no filter". The official frontend and MoviePilot both omit it rather than sending [].
        AddStrings(tags, "type", types);
        if (selection is { } filters)
        {
            // 官方前端把筛选状态原样交给 item/list：只有 type 是数组；题材是单个整数，
            // 其余各格是单数字段，而且请求键用单数 resolution / decade（tag/list 的返回键才是复数）。
            if (int.TryParse(filters.Genres.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var genre))
                tags["genres"] = genre;
            AddString(tags, "resolution", filters.Resolutions.FirstOrDefault());
            AddString(tags, "color_range", filters.ColorRanges.FirstOrDefault());
            AddString(tags, "audio_type", filters.AudioTypes.FirstOrDefault());
            AddString(tags, "locate", filters.Locations.FirstOrDefault());
            AddString(tags, "decade", filters.Decades.FirstOrDefault());
            AddString(tags, "recognition_status", filters.RecognitionStatuses.FirstOrDefault());
        }
        var result = new JsonObject
        {
            ["page"] = Math.Max(0, query.StartIndex) / Math.Max(1, query.Limit) + 1,
            ["page_size"] = Math.Max(1, query.Limit),
            ["sort_column"] = sort,
            ["sort_type"] = query.Descending ? "DESC" : "ASC",
            ["exclude_grouped_video"] = 1,
            ["tags"] = tags
        };
        if (!string.IsNullOrWhiteSpace(query.ParentId)) result["ancestor_guid"] = query.ParentId;
        // 播放状态这一位来自列表本身的固定条件（收藏页等），比面板上点的更具体，最后写。
        if (query.IsPlayed is { } played) tags["watched"] = played ? "1" : "0";
        else if (selection is { Watched.Length: > 0 } watched) tags["watched"] = watched.Watched;
        return result;
    }

    private static void AddStrings(JsonObject tags, string key, IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal))
            array.Add(value);
        if (array.Count > 0) tags[key] = array;
    }

    private static void AddString(JsonObject tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) tags[key] = value;
    }

    public async Task<FnNasFilterOptions> GetFilterOptionsAsync(string? parentId, bool favorites, string language, CancellationToken ct)
    {
        var tags = await client.GetFilterTagsAsync(connection, parentId, favorites, ct).ConfigureAwait(false);

        // 题材与国家/地区只是把原始值换成中文；映射表拿不到不该让整个筛选面板打不开。
        var genres = await OptionalReadAsync(() => client.GetGenreTagsAsync(connection, language, ct), "tag/genres", ct).ConfigureAwait(false);
        var countries = await OptionalReadAsync(() => client.GetCountryTagsAsync(connection, language, ct), "tag/iso3166", ct).ConfigureAwait(false);
        return FilterOptions(tags, genres, countries);
    }

    /// <summary>把 <c>tag/list</c> 与两张映射表装成面板能直接用的中文标签。</summary>
    public static FnNasFilterOptions FilterOptions(
        JsonObject tags, IReadOnlyList<JsonObject>? genres = null, IReadOnlyList<JsonObject>? countries = null)
    {
        // 真实服务器会在 iso3166 里重复同一个地区码（本次看到的 MD）；取第一份，不让一张映射表
        // 把整个筛选面板打成错误。
        var genreMap = Map(genres, "id");
        var countryMap = Map(countries, "key");

        return new FnNasFilterOptions(
            Options(tags, "genres", value => genreMap.GetValueOrDefault(value, value)),
            Options(tags, "resolutions", FnNasFilterLabels.Resolution),
            Options(tags, "color_range", FnNasFilterLabels.ColorRange),
            Options(tags, "audio_type", FnNasFilterLabels.AudioType),
            Options(tags, "locate", value => countryMap.GetValueOrDefault(value, value)),
            Options(tags, "decades", FnNasFilterLabels.Decade),
            Options(tags, "recognition_status", FnNasFilterLabels.RecognitionStatus));
    }

    private static List<FnNasTagOption> Options(JsonObject tags, string key, Func<string, string> label)
    {
        var result = new List<FnNasTagOption>();
        foreach (var value in Strings(tags[key]).Distinct(StringComparer.Ordinal))
            result.Add(new FnNasTagOption(value, label(value)));
        return result;
    }

    private static Dictionary<string, string> Map(IReadOnlyList<JsonObject>? values, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var id = Text(value, key);
            if (id.Length > 0) map.TryAdd(id, Text(value, "value"));
        }
        return map;
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
            // 卡片右上角的集数角标只给剧集：季/集的 local_number_of_episodes 是「这一季有几集」，写在那两张卡上
            // 会变成一句和这一页无关的话。剧集这一格是跨季合计（JOJO 本地两季共 3 集 → 3）。
            LocalEpisodeCount = type == EmbyItemType.Series ? Integer(value, "local_number_of_episodes") : null,
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
        // 集的 poster 是 16:9 的剧照（真实服务器 1920×1080），不是海报 —— 列表里它就是这一集能用的宽图。
        // 记成 Thumb，宽卡（继续观看／接下来看）和轮播才认得出来；Emby 那边这个位置本来就是集的剧照。
        // 竖版海报不进来：那正是轮播明确不要的那一种（ItemArtwork.BannerOrder 里没有 Primary）。
        else if (width is > 0 && height is > 0 && width > height) item.ImageTags["Thumb"] = poster;
        item.BackdropImageTags = Strings(value["backdrops"]).ToList();
        // 横幅海报右下角的清晰度角标；详情页的流信息仍走 stream/list，这里只读列表已经带上的档位。
        if (value["media_stream"] is JsonObject media) item.Resolutions = Strings(media["resolutions"]).ToList();
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
                ResolutionType = Text(stream, "resolution_type") is { Length: > 0 } resolution ? resolution : null,
                BitDepth = Integer(stream, "bit_depth"),
                RealFrameRate = FrameRate(Text(stream, "r_frame_rate")),
                // 飞牛的动态范围是两个字段：color_range_type 写 SDR / HDR10 / HLG / DolbyVision（列表接口
                // 里是数组、流接口里是字符串，两种都认），dv_profile 是 Dolby Vision 的档位（0＝没有 DV）。
                // 折成 Emby 那套字面量，HDR/DV 角标才判得出来（见 MediaStream.DynamicRangeLabel）。
                VideoRange = Strings(stream["color_range_type"]).FirstOrDefault(),
                VideoRangeType = Integer(stream, "dv_profile") is > 0 ? "DOVI" : null,
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
        ["subtitle_guid"] = report.FnNasSelection?.SubtitleGuid(report.SubtitleStreamIndex) ?? FnNasPlaybackSelection.NoSubtitleGuid,
        ["resolution"] = report.FnNasSelection?.Resolution ?? "",
        ["bitrate"] = report.FnNasSelection?.Bitrate ?? 0,
        ["duration"] = WholeSeconds(report.FnNasSelection?.DurationTicks ?? 0),
        ["ts"] = WholeSeconds(report.PositionTicks)
    };

    /// <summary>
    /// 整数秒，向下取整、负值钳到 0。<c>ts</c> 必须走这条路：飞牛服务端按整数解析它，收到小数（<c>5.0123456</c>）
    /// 会在内部转换时抛错、统一回业务码 <c>-4</c>（「服务器内部错误」）—— 2026-10-25「进度上报全部失败、续播点
    /// 永远是 0」就是这么来的；起播那条 <c>ts=0</c> 反而一直成功。官方前端喂的也是
    /// <c>Math.floor(player.currentTime)</c>，所以取整方向也照它来。<c>duration</c> 同理，别让将来某个小数时长
    /// 把整份上报带下水。
    /// </summary>
    private static long WholeSeconds(long ticks) => ticks <= 0 ? 0 : ticks / TimeSpan.TicksPerSecond;

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
