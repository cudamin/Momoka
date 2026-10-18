using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

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
            _ => ""
        }).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return mapped.Length == 1 ? mapped[0] : null;
    }

    public static EmbyItem Library(JsonObject value)
    {
        var item = Item(value);
        item.Type = EmbyItemType.CollectionFolder;
        item.IsFolder = true;
        item.CollectionType = Text(value, "type") switch
        {
            "movie" => "movies",
            "show" => "tvshows",
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
            "clip" => EmbyItemType.Video,
            "collection" => EmbyItemType.BoxSet,
            "" => EmbyItemType.Folder,
            _ => EmbyItemType.Video
        };

        var duration = Number(value, "duration");
        var position = Number(value, "viewOffset") ?? 0;
        var item = new EmbyItem
        {
            Id = Text(value, "ratingKey"),
            Name = Text(value, "title"),
            Type = type,
            IsFolder = type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.BoxSet or EmbyItemType.Folder,
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
            SeriesId = Blank(Text(value, "grandparentRatingKey")),
            SeriesName = Blank(Text(value, "grandparentTitle")),
            SeasonId = type == EmbyItemType.Episode ? Blank(Text(value, "parentRatingKey")) : null,
            ChildCount = Integer(value, "leafCount"),
            Genres = Tags(value, "Genre"),
            Studios = [.. Tags(value, "Studio").Select(name => new EmbyStudio { Name = name })],
            Tags = Tags(value, "Label"),
            UserData = new EmbyUserData
            {
                Played = (Number(value, "viewCount") ?? 0) > 0,
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
    /// The files behind one item, and the tracks inside them.
    /// <para>
    /// Plex nests <c>Media</c> → <c>Part</c> → <c>Stream</c>, and every part is a playable file, so one
    /// item can legitimately produce several sources. The part's <c>key</c> is the address the file is
    /// fetched from — a path, not a URL, which is why the token stays in the request headers.
    /// </para>
    /// </summary>
    public static List<MediaSource> Sources(JsonObject value, string itemId, PlexClient client, PlexConnection connection)
    {
        var result = new List<MediaSource>();
        foreach (var media in Objects(value, "Media"))
            foreach (var part in Objects(media, "Part"))
            {
                var key = Text(part, "key");
                if (key.Length == 0) continue;
                var source = new MediaSource
                {
                    Id = Text(part, "id"),
                    ItemId = itemId,
                    Path = Blank(Text(part, "file")),
                    Name = Path.GetFileName(Text(part, "file")),
                    Container = Blank(Text(part, "container")),
                    Size = Number(part, "size") is { } size ? (long)size : null,
                    Bitrate = Integer(media, "bitrate"),
                    RunTimeTicks = Number(media, "duration") is { } ms ? Ticks(ms) : null,
                    DirectStreamUrl = client.GetMediaUrl(connection, key),
                    SupportsDirectPlay = true,
                    SupportsDirectStream = true
                };
                AddStreams(source, part);
                source.DefaultAudioStreamIndex = source.MediaStreams.FirstOrDefault(stream => stream.IsAudio && stream.IsDefault)?.Index;
                source.DefaultSubtitleStreamIndex = source.MediaStreams.FirstOrDefault(stream => stream.IsSubtitle && stream.IsDefault)?.Index;
                result.Add(source);
            }
        return result;
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
                BitRate = Integer(stream, "bitrate"),
                BitDepth = Integer(stream, "bitDepth"),
                VideoRange = Blank(Text(stream, "videoRange")),
                AspectRatio = Blank(Text(stream, "displayAspectRatio")),
                IsDefault = Flag(stream, "default"),
                IsForced = Flag(stream, "forced"),
                IsExternal = delivery is not null,
                DeliveryUrl = delivery
            });
        }
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
        var poster = FirstNonEmpty(Text(value, "thumb"), Text(value, "parentThumb"), Text(value, "grandparentThumb"));
        var wide = FirstNonEmpty(Text(value, "art"), Text(value, "parentArt"), Text(value, "grandparentArt"));
        if (poster.Length > 0) item.ImageTags["Primary"] = poster;
        if (wide.Length > 0)
        {
            item.ImageTags["Thumb"] = wide;
            item.BackdropImageTags.Add(wide);
        }
        // An episode's thumb is a 16:9 still and everything else's is a 2:3 poster. Saying which is what
        // lets a library card decide between a poster grid and a wide strip before the image arrives.
        item.PrimaryImageAspectRatio = type == EmbyItemType.Episode ? 16d / 9d : 2d / 3d;
    }

    private static string FirstNonEmpty(params string[] values) =>
        Array.Find(values, value => value.Length > 0) ?? "";

    private static string? Blank(string value) => value.Length == 0 ? null : value;

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
        Objects(container, "Metadata").Concat(Objects(container, "Directory")).Concat(Objects(container, "Video"));
}
