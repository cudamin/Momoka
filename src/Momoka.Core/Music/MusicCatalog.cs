using Momoka.Emby;

namespace Momoka.Music;

public enum MusicSection { Tracks, Albums, Artists, Playlists, Favorites }

/// <summary>Music-specific queries do not change the existing video search and recommendation scopes.</summary>
public static class MusicCatalog
{
    public const int PageSize = 80;

    /// <summary>曲目表一行的封面边长。方形，与专辑封面同一形状，只是小两档。</summary>
    public const int TrackArtWidth = 44;

    /// <summary>封面墙一格的边长。方块唱片，不是 2:3 的海报。</summary>
    public const int WallTileWidth = 170;

    public static bool IsAudio(string? type) => string.Equals(type, EmbyItemType.Audio, StringComparison.OrdinalIgnoreCase);
    public static bool IsLibrary(string? type) => string.Equals(type, "music", StringComparison.OrdinalIgnoreCase);
    public static bool IsContainer(string? type) => type is EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist;

    public static ItemQuery Query(string? rootId, string? containerId, string? containerType,
        MusicSection section, int start, string? search)
    {
        var artist = containerType == EmbyItemType.MusicArtist;
        var type = section switch
        {
            MusicSection.Albums => EmbyItemType.MusicAlbum,
            MusicSection.Artists => EmbyItemType.MusicArtist,
            MusicSection.Playlists => EmbyItemType.Playlist,
            _ => EmbyItemType.Audio
        };
        return new ItemQuery
        {
            ParentId = containerId ?? rootId,
            ParentItemType = containerType ?? EmbyItemType.CollectionFolder,
            IncludeItemTypes = [type],
            ArtistIds = artist && containerId is { Length: > 0 }
                ? [containerId.StartsWith("fnmusic:artist:", StringComparison.Ordinal) ? containerId["fnmusic:artist:".Length..] : containerId] : [],
            Recursive = true,
            StartIndex = Math.Max(0, start),
            Limit = PageSize,
            SearchTerm = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            IsFavorite = section == MusicSection.Favorites,
            SortBy = containerType == EmbyItemType.MusicAlbum ? EmbySortBy.Number
                : containerType == EmbyItemType.Playlist ? "" : EmbySortBy.Name,
            Fields = EmbyFields.Browse + ",MediaSources,ParentId"
        };
    }

    public static string Subtitle(EmbyItem item) => string.Join(" · ",
        new[] { string.Join(" / ", item.Artists), item.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// 曲目表第二行。专辑容器里那一页的标题就是专辑名，再写一遍是同一句话说两次，所以只留艺术家；
    /// 整库的歌曲列表里专辑是唯一的上下文，照旧「艺术家 · 专辑」。
    /// </summary>
    public static string RowSubtitle(EmbyItem item, bool insideAlbum) => insideAlbum
        ? ArtistLine(item)
        : Subtitle(item);

    /// <summary>艺术家那一串。多艺术家用斜杠分开，和底部播放栏同一写法。</summary>
    public static string ArtistLine(EmbyItem item) => string.Join(" / ", item.Artists);

    /// <summary>
    /// 封面墙第二行。专辑写艺术家（专辑名已经是标题）；艺术家和歌单没有一句稳定可说的话 ——
    /// 服务器的 ChildCount 在各家协议里含义不同，不拿它编一个数字出来。
    /// </summary>
    public static string WallSubtitle(EmbyItem item) => item.Type switch
    {
        EmbyItemType.MusicAlbum => item.AlbumArtists.Count > 0
            ? string.Join(" / ", item.AlbumArtists.Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name)))
            : ArtistLine(item),
        _ => ""
    };

    /// <summary>
    /// 哪几个分区是一面方形封面墙。歌曲和收藏是曲目表 —— 收藏的是歌曲，不是专辑。
    /// 这条判断同时决定卡片的尺寸（墙上一格 170，表里一行 44），所以它住在 Core 里、由测试钉着。
    /// </summary>
    public static bool UsesCoverWall(MusicSection section) =>
        section is MusicSection.Albums or MusicSection.Artists or MusicSection.Playlists;

    /// <summary>封面墙一格要多高：方块封面加底下那两行字。</summary>
    public static double WallTileHeight => WallTileWidth + Infrastructure.CardSize.Chrome;

    /// <summary>页头读数后面的量词。歌曲数「首」、专辑数「张」，不是一律「项」。</summary>
    public static string CountUnit(MusicSection section) => section switch
    {
        MusicSection.Albums => "张",
        MusicSection.Artists => "位",
        MusicSection.Playlists => "个",
        _ => "首"
    };

    /// <summary>页头那一行读数，例如「492 首」。</summary>
    public static string CountLabel(MusicSection section, int total) => $"{total} {CountUnit(section)}";

    /// <summary>
    /// 播放页进度条中间那枚格式角标。容器（<c>flac</c>、<c>mp3</c>…）优先，没有就退到音轨编码；
    /// 两样都没有时返回空串，页面把角标收起 —— 不拿文件名后缀去猜。
    /// </summary>
    public static string FormatLabel(EmbyItem item)
    {
        var source = item.DefaultMediaSource;
        var container = source?.Container?.Trim();
        if (!string.IsNullOrEmpty(container)) return container.ToUpperInvariant();
        var codec = source?.MediaStreams
            .FirstOrDefault(stream => string.Equals(stream.Type, "Audio", StringComparison.OrdinalIgnoreCase))?.Codec?.Trim();
        return string.IsNullOrEmpty(codec) ? "" : codec.ToUpperInvariant();
    }
}
