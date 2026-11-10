namespace Momoka.LocalMedia;

public enum LocalMediaType { Movie, Episode }
public enum LocalMediaLinkMode { HardLink, SymbolicLink, Rename }
public enum LocalMediaOutputKind { VideoLink, SubtitleLink, MovieNfo, EpisodeNfo, SeriesNfo, Poster, Backdrop, EpisodeImage }

public sealed record LocalMediaMetadata(string Title, int? Year, LocalMediaType MediaType, string MediaSource, string MediaId)
{
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string EpisodeId { get; init; } = "";
    public string EpisodeTitle { get; init; } = "";
    public string EpisodeOverview { get; init; } = "";
    public string Overview { get; init; } = "";
    public string PosterUrl { get; init; } = "";
    public string BackdropUrl { get; init; } = "";
    public string EpisodeImageUrl { get; init; } = "";
    public DateOnly? Premiered { get; init; }
    public DateOnly? EpisodeAired { get; init; }
}

public sealed record LocalMediaSidecar(string Path, LocalMediaOutputKind Kind, string SourcePath = "", string ImageUrl = "");
public sealed record LocalMediaItemResult(string Path, LocalMediaOutputKind Kind, bool Success, string Message);
public sealed record LocalMediaResult(IReadOnlyList<LocalMediaItemResult> Items, bool Cancelled)
{
    public bool Success => !Cancelled && Items.Count > 0 && Items.All(item => item.Success);
    public string Summary => $"完成 {Items.Count(item => item.Success)} 项，失败 {Items.Count(item => !item.Success)} 项" +
        (Cancelled ? "，已取消后续操作" : "");
}

public sealed class LocalMediaPlan
{
    internal LocalMediaPlan(LocalMediaSource source, string targetRoot, string targetPath, LocalMediaMetadata metadata,
        LocalMediaLinkMode mode, IReadOnlyList<LocalMediaSidecar> sidecars)
    {
        Source = source;
        TargetRoot = targetRoot;
        TargetPath = targetPath;
        Metadata = metadata;
        Mode = mode;
        Sidecars = sidecars;
    }
    public LocalMediaSource Source { get; }
    public string TargetRoot { get; }
    public string TargetPath { get; }
    public LocalMediaMetadata Metadata { get; }
    public LocalMediaLinkMode Mode { get; }
    public IReadOnlyList<LocalMediaSidecar> Sidecars { get; }
    internal IReadOnlyList<LocalMediaSnapshot> Snapshots { get; init; } = [];
    private int _consumed;
    internal bool Consume() => Interlocked.Exchange(ref _consumed, 1) == 0;
}

internal sealed record LocalMediaSnapshot(string Path, long Length, DateTime LastWriteTimeUtc, string Hash, string Identity);
