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
    string Format = "");

public sealed record AniBtMatch(
    IReadOnlyList<AniBtAnime> Candidates,
    AniBtAnime? AutomaticCandidate,
    string DecisionKind,
    string Confidence,
    string Reason,
    int? StructureSeason);

/// <summary>公开 Bangumi 搜索没有分页；HasMore 不会把达到条数上限误称为可翻页。</summary>
public sealed record AniBtSearchPage(IReadOnlyList<AniBtAnime> Items, bool HasMore);

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
    string Resolution = "");

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
