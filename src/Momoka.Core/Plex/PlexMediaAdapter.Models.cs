using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Plex;

public sealed partial class PlexMediaAdapter
{
    /// <summary>
    /// The sort keys this client can express against Plex. The same four the 飞牛 backend offers, so the
    /// sort menu does not grow or shrink as the user switches servers.
    /// </summary>
    public static bool SupportsSort(string sort) => sort is EmbySortBy.Name or EmbySortBy.DateAdded
        or EmbySortBy.ReleaseDate or EmbySortBy.Rating or EmbySortBy.Random;

    /// <summary>
    /// A browse request, as Plex query pairs. Paging is deliberately absent: Plex takes it as two headers,
    /// and those belong to the transport rather than to a query string (see <see cref="PlexClient"/>).
    /// </summary>
    public static IReadOnlyList<PlexParameter> Query(ItemQuery query)
    {
        var sort = query.SortBy switch
        {
            EmbySortBy.Name => "titleSort",
            EmbySortBy.DateAdded => "addedAt",
            EmbySortBy.ReleaseDate => "originallyAvailableAt",
            EmbySortBy.Rating => "rating",
            EmbySortBy.Random => "random",
            _ => throw new NotSupportedException("Plex 不支持此排序方式")
        };

        var pairs = new List<PlexParameter> { new("sort", query.Descending ? sort + ":desc" : sort) };
        if (Type(query.IncludeItemTypes) is { Length: > 0 } type) pairs.Add(new("type", type));
        // Plex filters watched state with one key rather than Emby's Filters list: 1 keeps the unwatched,
        // 0 keeps the watched.
        if (query.IsPlayed is { } played) pairs.Add(new("unwatched", played ? "0" : "1"));
        return pairs;
    }

    /// <summary>
    /// Plex's numeric type for a browse request, or null to leave the whole library on offer.
    /// <para>
    /// Null for anything but exactly one mappable type, on purpose. Plex's <c>type</c> takes one value,
    /// and the type lists this client sends are sometimes several (a movies library asks for
    /// <c>Movie,Video,BoxSet</c>) — sending the first of those would silently hide every file the server
    /// failed to match to a film, which is exactly the set worth seeing.
    /// </para>
    /// </summary>
    private static string? Type(IReadOnlyList<string> types)
    {
        var mapped = types.Select(type => type switch
        {
            EmbyItemType.Movie => "1",
            EmbyItemType.Series => "2",
            EmbyItemType.Season => "3",
            EmbyItemType.Episode => "4",
            EmbyItemType.MusicArtist => "8",
            EmbyItemType.MusicAlbum => "9",
            EmbyItemType.Audio => "10",
            _ => ""
        }).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return mapped.Length == 1 ? mapped[0] : null;
    }

    public static EmbyItem Library(JsonObject value)
    {
        // Library sections use key; media entries keep ratingKey in Item.
        var id = Text(value, "key");
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Plex 媒体库缺少 section key");

        var item = Item(value);
        item.Id = id;
        item.Type = EmbyItemType.CollectionFolder;
        item.IsFolder = true;
        item.CollectionType = Text(value, "type") switch
        {
            "movie" => "movies",
            "show" => "tvshows",
            "artist" => "music",
            _ => "homevideos"
        };
        return item;
    }

    public static EmbyItem Item(JsonObject value)
    {
        var native = Text(value, "type");
        var type = native switch
        {
            "movie" => EmbyItemType.Movie,
            "show" => EmbyItemType.Series,
            "season" => EmbyItemType.Season,
            "episode" => EmbyItemType.Episode,
            "artist" => EmbyItemType.MusicArtist,
            "album" => EmbyItemType.MusicAlbum,
            "track" => EmbyItemType.Audio,
            "playlist" => EmbyItemType.Playlist,
            "clip" => EmbyItemType.Video,
            "collection" => EmbyItemType.BoxSet,
            "" => EmbyItemType.Folder,
            _ => EmbyItemType.Video
        };

        var duration = Number(value, "duration");
        var position = Number(value, "viewOffset") ?? 0;
        var container = type is EmbyItemType.Series or EmbyItemType.Season;
        var childCount = container ? LeafCount(value, "leafCount") : Integer(value, "leafCount");
        var viewedCount = container ? LeafCount(value, "viewedLeafCount") : null;
        // 剧/季按已看叶子数判定；空容器或未知/矛盾统计不能当作全部看完。
        var remaining = container && childCount is { } total && viewedCount is { } viewed && viewed <= total
            ? total - viewed : (int?)null;
        var item = new EmbyItem
        {
            Id = Text(value, "ratingKey"),
            Name = Text(value, "title"),
            Type = type,
            IsFolder = type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.BoxSet or EmbyItemType.Folder
                or EmbyItemType.MusicArtist or EmbyItemType.MusicAlbum or EmbyItemType.Playlist,
            OriginalTitle = Blank(Text(value, "originalTitle")),
            Overview = Blank(Text(value, "summary")),
            SortName = Blank(Text(value, "titleSort")),
            PremiereDate = Date(Text(value, "originallyAvailableAt")),
            DateCreated = Seconds(Number(value, "addedAt")),
            RunTimeTicks = duration is { } ms ? Ticks(ms) : null,
            CommunityRating = Number(value, "rating") is { } rating ? (float)rating : null,
            OfficialRating = Blank(Text(value, "contentRating")),
            IndexNumber = Integer(value, "index"),
            ParentIndexNumber = Integer(value, "parentIndex"),
            ParentId = Blank(Text(value, "parentRatingKey")),
            SeriesId = type == EmbyItemType.Episode ? Blank(Text(value, "grandparentRatingKey")) : null,
            SeriesName = type == EmbyItemType.Episode ? Blank(Text(value, "grandparentTitle")) : null,
            SeasonId = type == EmbyItemType.Episode ? Blank(Text(value, "parentRatingKey")) : null,
            ChildCount = childCount,
            Genres = Tags(value, "Genre"),
            Studios = [.. Tags(value, "Studio").Select(name => new EmbyStudio { Name = name })],
            Tags = Tags(value, "Label"),
            UserData = new EmbyUserData
            {
                Played = container ? childCount is > 0 && remaining == 0 : (Number(value, "viewCount") ?? 0) > 0,
                UnplayedItemCount = remaining,
                PlaybackPositionTicks = Ticks(position),
                PlayedPercentage = duration is > 0 ? position / duration.Value * 100 : null,
                LastPlayedDate = Seconds(Number(value, "lastViewedAt"))
            }
        };

        item.ProductionYear = Integer(value, "year") ?? item.PremiereDate?.Year;
        AddProviderIds(item, value);
        AddArtwork(item, value, type);
        return item;
    }

    /// <summary>
    /// Plex 的章节表：<c>includeChapters=1</c> 时随条目一起发，<c>startTimeOffset</c> 是毫秒。
    /// 只有起点和名字，用途和 Emby 的章节一样（走带刻度、悬浮预览、播放器兜底），不带章节图。
    /// </summary>
    public static List<ChapterInfo> Chapters(JsonObject value)
    {
        var chapters = new List<ChapterInfo>();
        foreach (var chapter in Elements(value, "Chapter"))
        {
            if (Number(chapter, "startTimeOffset") is not { } start || start < 0) continue;
            chapters.Add(new ChapterInfo
            {
                StartPositionTicks = Ticks(start),
                Name = Blank(Text(chapter, "tag")) ?? Blank(Text(chapter, "title"))
            });
        }

        chapters.Sort((left, right) => left.StartPositionTicks.CompareTo(right.StartPositionTicks));
        return chapters;
    }

    /// <summary>
    /// Plex 的片头片尾 marker：<c>includeMarkers=1</c> 时随条目一起发，<c>type</c> 是 <c>intro</c> 或
    /// <c>credits</c>，offset 是毫秒。广告（<c>commercial</c>）不在这里管 —— 跳过按钮只有一种动作。
    /// <para>
    /// 一部片可能有一条 intro 和先后两条 credits（片中彩蛋前一条、片尾名单前一条）。两条都留着：
    /// 播放走到哪一条就按哪一条跳，和 Plex 自己的「跳过片尾」一致。
    /// </para>
    /// </summary>
    public static List<ServerSkipMarker> Markers(JsonObject value)
    {
        var markers = new List<ServerSkipMarker>();
        foreach (var marker in Elements(value, "Marker"))
        {
            var kind = Text(marker, "type") switch
            {
                "intro" => ServerSkipMarkerKind.Opening,
                "credits" => ServerSkipMarkerKind.Ending,
                _ => (ServerSkipMarkerKind?)null
            };
            if (kind is not { } found) continue;
            if (Number(marker, "startTimeOffset") is not { } start || start < 0) continue;

            var end = Number(marker, "endTimeOffset") is { } finish && finish > start ? finish : 0;
            markers.Add(new ServerSkipMarker(found, start / 1000d, end / 1000d));
        }

        markers.Sort((left, right) => left.StartSeconds.CompareTo(right.StartSeconds));
        return markers;
    }

    /// <summary>Plex 的元素：重复出现时是数组，只有一个时可能是单个对象。</summary>
    internal static IEnumerable<JsonObject> Elements(JsonObject value, string key) => value[key] switch
    {
        JsonArray array => array.OfType<JsonObject>(),
        JsonObject single => [single],
        _ => []
    };

    /// <summary>
    /// The files behind one item, and the tracks inside them.
    /// <para>
    /// Each Media is a version; its Parts are sequential files on that version's timeline.
    /// Part keys remain credential-free addresses, authenticated at the transport boundary.
    /// </para>
    /// </summary>
    public static List<MediaSource> Sources(JsonObject value, string itemId, PlexClient client, PlexConnection connection)
    {
        var result = new List<MediaSource>();
        foreach (var media in Elements(value, "Media"))
        {
            var parts = Elements(media, "Part").ToArray();
            if (parts.Length == 0) continue;
            var first = parts[0];
            if (parts.Any(part => string.IsNullOrWhiteSpace(Text(part, "key")))) continue;
            var source = new MediaSource
            {
                Id = parts.Length == 1 ? Text(first, "id")
                    : "media:" + (Blank(Text(media, "id")) ?? "part:" + Text(first, "id")),
                ItemId = itemId,
                Path = Blank(Text(first, "file")),
                Name = Path.GetFileName(Text(first, "file")),
                Container = Blank(FirstNonEmpty(Text(first, "container"), Text(media, "container"))),
                Size = Number(first, "size") is { } size ? (long)size : null,
                Bitrate = Bitrate(media),
                RunTimeTicks = Number(media, "duration") is { } ms ? Ticks(ms) : null,
                DirectStreamUrl = client.GetMediaUrl(connection, Text(first, "key")),
                SupportsDirectPlay = true,
                SupportsDirectStream = true
            };
            AddStreams(source, first);
            if (parts.Length > 1)
            {
                var timeline = new List<PlaybackPart>(parts.Length);
                long duration = 0;
                var valid = true;
                foreach (var part in parts)
                {
                    // EDL guesses a one-second segment when native duration is also unknown. Never expose
                    // that invented timeline, nor keep just the first Part as a supposedly whole version.
                    var milliseconds = Number(part, "duration");
                    if (milliseconds is not > 0 || !double.IsFinite(milliseconds.Value)
                        || milliseconds.Value >= long.MaxValue / (double)TimeSpan.TicksPerMillisecond)
                    { valid = false; break; }
                    var ticks = Ticks(milliseconds.Value);
                    if (ticks <= 0 || ticks > long.MaxValue - duration) { valid = false; break; }
                    var partSource = new MediaSource();
                    AddStreams(partSource, part);
                    if (!InternalLayout(source).SequenceEqual(InternalLayout(partSource))) { valid = false; break; }
                    timeline.Add(new PlaybackPart(client.GetMediaUrl(connection, Text(part, "key")), ticks));
                    duration += ticks;
                }
                if (!valid)
                {
                    Log.Warn("plex", "Plex 多段版本缺少有效分段时长或内置轨道布局不一致，略过该版本");
                    continue;
                }
                // Part-local sidecar subtitles need their own parallel timeline. They are inapplicable
                // to this whole-version resource; keep embedded tracks and all ordinary single-file tracks.
                source.MediaStreams.RemoveAll(stream => stream.IsExternal);
                source.MediaParts = timeline;
                source.RunTimeTicks = duration;
                source.Size = TotalSize(parts);
            }
            source.DefaultAudioStreamIndex = source.MediaStreams.FirstOrDefault(stream => stream.IsAudio && stream.IsDefault)?.Index;
            source.DefaultSubtitleStreamIndex = source.MediaStreams.FirstOrDefault(stream => stream.IsSubtitle && stream.IsDefault)?.Index;
            result.Add(source);
        }
        return result;
    }

    private static IEnumerable<(string Type, string? Codec, string? Language, string? Title, bool Forced)> InternalLayout(MediaSource source) =>
        source.MediaStreams.Where(stream => !stream.IsExternal).OrderBy(stream => stream.Index)
            .Select(stream => (stream.Type, stream.Codec, stream.Language, stream.Title, stream.IsForced));

    private static long? TotalSize(IEnumerable<JsonObject> parts)
    {
        long total = 0;
        foreach (var part in parts)
        {
            if (Number(part, "size") is not { } size || size < 0 || !double.IsFinite(size)
                || size >= long.MaxValue || size > long.MaxValue - total) return null;
            total += (long)size;
        }
        return total;
    }

    private static void AddStreams(MediaSource source, JsonObject part)
    {
        foreach (var stream in Objects(part, "Stream"))
        {
            var type = Text(stream, "streamType") switch { "1" => "Video", "2" => "Audio", "3" => "Subtitle", _ => "" };
            if (type.Length == 0 || Integer(stream, "id") is not { } index) continue;
            // A subtitle stream with a key is one Plex serves as a standalone file; without one it lives
            // inside the container and can only be selected there.
            var delivery = type == "Subtitle" ? Blank(Text(stream, "key")) : null;
            source.MediaStreams.Add(new MediaStream
            {
                Index = index,
                Type = type,
                Codec = Blank(Text(stream, "codec")),
                Profile = Blank(Text(stream, "profile")),
                Language = Blank(Text(stream, "language")),
                Title = Blank(Text(stream, "title")),
                DisplayTitle = Blank(Text(stream, "displayTitle")),
                ChannelLayout = Blank(Text(stream, "channelLayout")),
                Channels = Integer(stream, "channels"),
                Width = Integer(stream, "width"),
                Height = Integer(stream, "height"),
                BitRate = Bitrate(stream),
                BitDepth = Integer(stream, "bitDepth"),
                VideoRange = Blank(Text(stream, "videoRange")),
                VideoRangeType = DynamicRangeType(stream),
                AspectRatio = Blank(Text(stream, "displayAspectRatio")),
                IsDefault = Flag(stream, "default"),
                IsForced = Flag(stream, "forced"),
                IsExternal = delivery is not null,
                DeliveryUrl = delivery
            });
        }
    }

    private static int? LeafCount(JsonObject value, string key) =>
        Number(value, key) is >= 0 and <= int.MaxValue and var count && count == Math.Truncate(count)
            ? (int)count : null;

    // Plex 的 Media/Stream 用 kbps；共享模型用 bit/s。先扩大类型，避免大码率回绕为负数。
    private static int? Bitrate(JsonObject value)
    {
        if (Integer(value, "bitrate") is not { } kilobits) return null;
        var bits = (long)kilobits * 1000;
        return bits is >= int.MinValue and <= int.MaxValue ? (int)bits : null;
    }

    private static void AddProviderIds(EmbyItem item, JsonObject value)
    {
        foreach (var guid in Objects(value, "Guid"))
        {
            // "imdb://tt123", "tmdb://456", "tvdb://789" — the prefix is the scraper, the rest is its id.
            var id = Text(guid, "id");
            var separator = id.IndexOf("://", StringComparison.Ordinal);
            if (separator <= 0 || separator + 3 >= id.Length) continue;
            var key = id[..separator] switch { "imdb" => "Imdb", "tmdb" => "Tmdb", "tvdb" => "Tvdb", var other => other };
            item.ProviderIds[key] = id[(separator + 3)..];
        }
    }

    /// <summary>
    /// Plex's four artwork fields, folded onto the two slots the shell draws with.
    /// <para>
    /// <c>thumb</c> is the poster-shaped image and <c>art</c> the wide one. A season and an episode carry
    /// neither of their own, which is what the <c>parent*</c> and <c>grandparent*</c> pairs are for — the
    /// inherited-image walk Emby does with <c>ParentLogoItemId</c> is done here instead, because Plex
    /// sends the inherited path rather than an id to look it up by.
    /// </para>
    /// </summary>
    private static void AddArtwork(EmbyItem item, JsonObject value, string type)
    {
        var poster = FirstNonEmpty(Text(value, "thumb"), Text(value, "parentThumb"), Text(value, "grandparentThumb"),
            type == EmbyItemType.Playlist ? Text(value, "composite") : "");
        var wide = FirstNonEmpty(Text(value, "art"), Text(value, "parentArt"), Text(value, "grandparentArt"));
        if (poster.Length > 0) item.ImageTags["Primary"] = poster;
        if (wide.Length > 0)
        {
            item.ImageTags["Thumb"] = wide;
            item.BackdropImageTags.Add(wide);
        }
        // An episode's thumb is a 16:9 still and everything else's is a 2:3 poster. Saying which is what
        // lets a library card decide between a poster grid and a wide strip before the image arrives.
        item.PrimaryImageAspectRatio = type switch
        {
            EmbyItemType.Episode => 16d / 9d,
            EmbyItemType.Audio or EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist or EmbyItemType.Playlist => 1d,
            _ => 2d / 3d
        };
    }

    private static string FirstNonEmpty(params string[] values) =>
        Array.Find(values, value => value.Length > 0) ?? "";

    private static string? Blank(string value) => value.Length == 0 ? null : value;

    /// <summary>
    /// Plex 的动态范围细分写法，折成 Emby 那套 <c>VideoRangeType</c> 的字面量：新版本直接给
    /// <c>videoDynamicRangeType</c>（<c>DV HDR10</c> / <c>HDR10</c> / <c>DV</c>），老版本只有一个
    /// <c>DOVIPresent</c> 布尔。两样都没有就是 null —— 那时只剩 <c>videoRange</c> 那个粗档位可读。
    /// </summary>
    private static string? DynamicRangeType(JsonObject stream)
    {
        if (Blank(Text(stream, "videoDynamicRangeType")) is { } type) return type;
        return Flag(stream, "DOVIPresent") ? "DOVI" : null;
    }

    private static List<string> Tags(JsonObject value, string key) =>
        [.. Objects(value, key).Select(tag => Text(tag, "tag")).Where(tag => tag.Length > 0)];

    /// <summary>Unix seconds, which is how Plex writes <c>addedAt</c> and <c>lastViewedAt</c>.</summary>
    private static DateTimeOffset? Seconds(double? value) => value is > 0 and < 253402300799 and var seconds
        ? DateTimeOffset.FromUnixTimeSeconds((long)seconds) : null;

    /// <summary><c>originallyAvailableAt</c> is a bare date ("2016-04-17"), not a timestamp.</summary>
    private static DateTimeOffset? Date(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    internal static IEnumerable<JsonObject> Objects(JsonObject value, string key) =>
        value[key] is JsonArray array ? array.OfType<JsonObject>() : [];

    /// <summary>
    /// Every item in a container. Plex names the array after what is in it — <c>Metadata</c> for films and
    /// episodes, <c>Directory</c> for shows and seasons — and a container may hold more than one kind, so
    /// all the names are read rather than one being guessed at.
    /// </summary>
    internal static IEnumerable<JsonObject> Items(JsonObject container) =>
        Elements(container, "Metadata").Concat(Elements(container, "Directory")).Concat(Elements(container, "Video"))
            .Concat(Elements(container, "Track"));
}
