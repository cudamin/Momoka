namespace Momoka.MoviePilot;

/// <summary>本机影视文件的可信作品及单集元数据；文件操作由本机整理器执行。</summary>
public sealed record MoviePilotLocalRecognition
{
    public required MoviePilotMediaDetail Detail { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string? EpisodeId { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? EpisodeOverview { get; init; }
    public string? EpisodeAirDate { get; init; }
    public string? EpisodeImageUrl { get; init; }
}
