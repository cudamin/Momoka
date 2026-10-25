using System.Net;
using Momoka.MoviePilot;

namespace Momoka.AniBt;

/// <summary>Bangumi 身份来自 AniBT；它与 MoviePilot 的 TMDB 编号互不通用。</summary>
public sealed record AniBtAnime(
    string AnimeId,
    long BgmId,
    string Title,
    string OriginalTitle = "",
    string? ImageUrl = null,
    int? Year = null,
    int? EpisodeCount = null,
    string Format = "",
    string Status = "",
    double? Score = null,
    string Description = "",
    string EnglishTitle = "",
    string Season = "",
    string? OfficialSite = null)
{
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<AniBtExternalLink> ExternalLinks { get; init; } = [];
}

public sealed record AniBtExternalLink(string Label, Uri Uri);

public sealed record AniBtMatch(
    IReadOnlyList<AniBtAnime> Candidates,
    AniBtAnime? AutomaticCandidate,
    string DecisionKind,
    string Confidence,
    string Reason,
    int? StructureSeason)
{
    /// <summary>确认框默认选中的结果：明确的自动识别优先，否则直接显示第一个候选，不让输入框空着。</summary>
    public AniBtAnime? DefaultCandidate =>
        AutomaticCandidate ?? (Candidates.Count > 0 ? Candidates[0] : null);
}

/// <summary>目录搜索使用服务端返回的 opaque cursor，不从页号猜下一页。</summary>
public sealed record AniBtSearchPage(IReadOnlyList<AniBtAnime> Items, bool HasMore, string? Cursor = null);

/// <summary>资源使用最终规范化的 Bangumi 身份，避免旧编号合并后把资源交给另一份媒体元数据。</summary>
public sealed record AniBtReleasePage(long BgmId, IReadOnlyList<AniBtRelease> Items);

public sealed record AniBtRelease(
    string ReleaseId,
    string Title,
    Uri DownloadUri,
    Uri PageUri,
    long? Size = null,
    DateTimeOffset? PublishedAt = null,
    string GroupName = "",
    string EpisodeKey = "",
    string Resolution = "",
    string Format = "",
    IReadOnlyList<string>? Languages = null,
    string Subtitle = "",
    IReadOnlyList<string>? Tags = null,
    bool HasTorrent = true);

/// <summary>PublishedEpisodes 是服务端完整进度；Items 仅为该组最近最多 30 条发布。</summary>
public sealed record AniBtReleaseGroup(string GroupId, string Name, string Slug,
    IReadOnlyList<int> PublishedEpisodes, int TotalReleases, IReadOnlyList<AniBtRelease> Items, Uri? RssUri = null);

public sealed record AniBtGroupPage(AniBtAnime Anime, IReadOnlyList<AniBtReleaseGroup> Groups);

public sealed record AniBtBrowsePage(AniBtAnime Anime, IReadOnlyList<AniBtReleaseGroup> Groups,
    IReadOnlyList<AniBtResource> Resources);

/// <summary>
/// 一条 AniBT 发布与交给 MoviePilot 的下载资源。两半来自同一次 RSS 读取，序号一一对应：
/// 页面上的字幕组、集号与清晰度只用发布这一半，下载身份与防重状态仍只认资源这一半。
/// </summary>
public sealed record AniBtResource(AniBtRelease Release, MoviePilotResource Resource);

public sealed class AniBtException(
    string message,
    HttpStatusCode? statusCode = null,
    TimeSpan? retryAfter = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
