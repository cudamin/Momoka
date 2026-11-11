using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnMusic;

public sealed partial class FnMusicMediaAdapter
{
    private EmbyItem Item(JsonObject value, string type, JsonObject? audioSpec = null)
    {
        var guid = Text(value, "guid");
        if (guid.Length == 0) throw new EmbyApiException("飞牛音乐条目缺少标识");
        var audio = type == EmbyItemType.Audio;
        var album = value["album"] as JsonObject;
        var artists = Names(value["artists"]);
        var spec = audioSpec ?? value["audioSpec"] as JsonObject ?? new();
        var duration = audioSpec is not null && !Flag(value, "isCue") ? Number(spec, "duration") ?? Number(value, "duration") : Number(value, "duration");
        var item = new EmbyItem
        {
            Id = audio ? guid : ContainerPrefix + Family(type) + ":" + guid,
            Name = Text(value, audio ? "title" : "name"), Type = type, IsFolder = !audio,
            PrimaryImageAspectRatio = 1, CanEditItems = false,
            IndexNumber = Integer(value, "trackNo"), ParentIndexNumber = Integer(value, "discNo"),
            RunTimeTicks = Ticks(duration), ChildCount = Integer(value, "trackCount"),
            Album = album is null ? null : Text(album, "name"),
            AlbumId = album is null || Text(album, "guid").Length == 0 ? null : ContainerPrefix + "album:" + Text(album, "guid"),
            Artists = artists.Select(artist => artist.Name).ToList(), ArtistItems = artists,
            AlbumArtists = Names(album?["artists"]), UserData = audio ? new() { IsFavorite = Flag(value, "isFavorite") } : null,
            ProductionYear = Integer(value, "year") ?? (album is null ? Integer(value, "originalReleaseYear") : Integer(album, "originalReleaseYear")),
            DateCreated = Date(Number(value, "createdAt")), Path = audio ? Text(spec, "path") : null
        };
        var coverId = Text(value, "coverId");
        if (coverId.Length == 0 && album is not null) coverId = Text(album, "coverId");
        if (coverId.Length > 0) item.ImageTags["Primary"] = coverId;
        if (!audio) return item;
        var format = Text(spec, "format");
        if (format.Length == 0) format = System.IO.Path.GetExtension(item.Path)?.TrimStart('.') ?? "";
        var unsupported = Flag(value, "isCue") || format.ToLowerInvariant() is "cue" or "iso" or "sacd"
            || Number(value, "accessStatus") is not (null or 0) || Flag(value, "isAdminDeleted");
        if (unsupported) return item;
        var stream = new MediaStream { Type = "Audio", Index = 0, Codec = Text(spec, "codec"),
            Channels = Integer(spec, "channel"), BitDepth = Integer(spec, "bitDepth"), BitRate = Integer(spec, "bitrate") };
        item.MediaStreams = [stream];
        item.Size = Number(spec, "size") is > 0 and var size ? size : null;
        item.MediaSources = [new() { Id = guid, ItemId = guid, Name = item.Name, Path = item.Path, Container = format,
            RunTimeTicks = item.RunTimeTicks, Size = item.Size, Bitrate = Integer(spec, "bitrate"), SupportsDirectPlay = true,
            SupportsDirectStream = true, DefaultAudioStreamIndex = 0, MediaStreams = [stream],
            DirectStreamUrl = FnMusicEndpoint.Api(connection.Mount, "track/stream", ("guid", guid)) }];
        return item;
    }

    private static List<EmbyNamedId> Names(JsonNode? node) => node is JsonArray list ? list.OfType<JsonObject>()
        .Where(value => Text(value, "name").Length > 0).Select(value => new EmbyNamedId { Id = Text(value, "guid"), Name = Text(value, "name") }).ToList() : [];
    private static int? Integer(JsonObject value, string key) => Number(value, key) is >= int.MinValue and <= int.MaxValue and var number ? (int)number : null;
    private static long? Ticks(long? milliseconds) => milliseconds is > 0 and <= long.MaxValue / TimeSpan.TicksPerMillisecond
        ? milliseconds * TimeSpan.TicksPerMillisecond : null;
    private static DateTimeOffset? Date(long? seconds) => seconds is >= -62135596800 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;
}
