using System.Net;

namespace Momoka.Metadata;

/// <summary>TMDB 搜索结果；原名用于严格匹配，不能以搜索排序代替识别。</summary>
public sealed record TmdbMediaCandidate
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string OriginalTitle { get; init; } = "";
    public int Year { get; init; }
    public bool IsSeries { get; init; }
    /// <summary>首屏未包含全部候选时保留手选，禁止宣称是唯一自动匹配。</summary>
    public bool AutoMatchEligible { get; init; } = true;
    public string Overview { get; init; } = "";
    public string PosterUrl { get; init; } = "";
    public string BackdropUrl { get; init; } = "";
}

/// <summary>本机整理所需的纯文本元数据。Id始终是电影/电视剧ID，EpisodeId单独保存。</summary>
public sealed record TmdbLocalMetadata
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string OriginalTitle { get; init; } = "";
    public int Year { get; init; }
    public bool IsSeries { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public string EpisodeId { get; init; } = "";
    public string EpisodeTitle { get; init; } = "";
    public string Overview { get; init; } = "";
    public string EpisodeOverview { get; init; } = "";
    public string PosterUrl { get; init; } = "";
    public string BackdropUrl { get; init; } = "";
    public string EpisodeImageUrl { get; init; } = "";
    public string EpisodeAired { get; init; } = "";
    public string Premiered { get; init; } = "";
}

/// <summary>安全的用户可见错误；不携带请求URL、令牌或服务端响应文本。</summary>
public sealed class TmdbException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
