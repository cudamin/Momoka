namespace Momoka.LocalMedia;

public enum LocalMediaType { Movie, Episode }
// 数值是旧 local-library.json 的持久格式，不能重排：0 仍是视频，2-7 仍是 NFO 与图片。
// 1 是已退役的字幕链接槽位，保留给旧索引解析，不再产生。
public enum LocalMediaOutputKind
{
    Video = 0,
    Subtitle = 1,
    MovieNfo = 2,
    EpisodeNfo = 3,
    SeriesNfo = 4,
    Poster = 5,
    Backdrop = 6,
    EpisodeImage = 7,
    PersonImage = 8
}

public sealed record LocalMediaMetadata(string Title, int? Year, LocalMediaType MediaType, string MediaSource, string MediaId)
{
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string EpisodeId { get; init; } = "";
    public string EpisodeTitle { get; init; } = "";
    public string EpisodeOverview { get; init; } = "";
    public string Overview { get; init; } = "";
    public string Tagline { get; init; } = "";
    public double? CommunityRating { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string[] Genres { get; init; } = [];
    public string[] Studios { get; init; } = [];
    public LocalMediaPerson[] People { get; init; } = [];
    public string PosterUrl { get; init; } = "";
    public string BackdropUrl { get; init; } = "";
    public string EpisodeImageUrl { get; init; } = "";
    public DateOnly? Premiered { get; init; }
    public DateOnly? EpisodeAired { get; init; }
}

/// <summary>
/// 本机条目带得动的演职人员：姓名、角色、是否导演，TMDB 人物编号、头像来源与落盘后的头像路径。
/// ImageUrl 只活在这一次计划里，写进索引前会清掉；ImagePath 才是详情页读的那一份。
/// </summary>
public sealed record LocalMediaPerson(string Name, string Role, bool Director)
{
    public string Id { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string ImagePath { get; init; } = "";
}

/// <summary>Optional 的附属文件失败不算「未完成」：演职人员头像只是锦上添花。</summary>
public sealed record LocalMediaSidecar(string Path, LocalMediaOutputKind Kind, string ImageUrl = "", bool Optional = false);
public sealed record LocalMediaItemResult(string Path, LocalMediaOutputKind Kind, bool Success, string Message, bool Optional = false)
{
    public bool Satisfied => Success || Optional;
}
public sealed record LocalMediaResult(IReadOnlyList<LocalMediaItemResult> Items, bool Cancelled)
{
    public bool Success => !Cancelled && Items.Count > 0 && Items.All(item => item.Satisfied);
    // 可选附属文件（演职人员头像）失败不算失败：那一项只是少一张脸，条目仍是完整的。
    public string Summary => $"完成 {Items.Count(item => item.Success)} 项，失败 {Items.Count(item => !item.Satisfied)} 项" +
        (Cancelled ? "，已取消后续操作" : "");
}

/// <summary>一次原地入库：视频留在源路径，NFO 与图片写进本机元数据目录。</summary>
public sealed class LocalMediaPlan
{
    internal LocalMediaPlan(LocalMediaSource source, string videoPath, LocalMediaMetadata metadata,
        IReadOnlyList<LocalMediaSidecar> sidecars)
    {
        Source = source;
        VideoPath = videoPath;
        Metadata = metadata;
        Sidecars = sidecars;
    }
    public LocalMediaSource Source { get; }
    public string VideoPath { get; }
    public LocalMediaMetadata Metadata { get; }
    public IReadOnlyList<LocalMediaSidecar> Sidecars { get; }
    internal IReadOnlyList<LocalMediaSnapshot> Snapshots { get; init; } = [];
    private int _consumed;
    internal bool Consume() => Interlocked.Exchange(ref _consumed, 1) == 0;
}

internal sealed record LocalMediaSnapshot(string Path, long Length, DateTime LastWriteTimeUtc, string Hash, string Identity);
