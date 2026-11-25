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
    public string Tagline { get; init; } = "";
    public double Rating { get; init; }
    public int Runtime { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Studios { get; init; } = [];
    public IReadOnlyList<TmdbLocalPerson> People { get; init; } = [];
}

/// <summary>
/// 详情页演职人员行需要的一份署名。Id 是 TMDB 人物编号（同一署名去重与头像落盘都用它），
/// ImageUrl 是 TMDB 头像地址；没有头像的人照样保留，只把这一项留空。
/// </summary>
public sealed record TmdbLocalPerson(string Name, string Role, bool Director)
{
    public string Id { get; init; } = "";
    public string ImageUrl { get; init; } = "";
}

/// <summary>安全的用户可见错误；不携带请求URL、令牌或服务端响应文本。</summary>
public sealed class TmdbException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>本机文件自动识别：依次尝试整串片名与中英分段关键词，只接受唯一精确匹配。</summary>
public static class TmdbLocalMedia
{
    public static async Task<TmdbMediaCandidate?> FindUniqueAsync(TmdbClient client, ParsedLocalMediaName parsed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(parsed);
        if (parsed.Problem.Length > 0 || parsed.Title.Length == 0) return null;
        foreach (var query in LocalMediaName.SearchQueries(parsed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = await client.SearchAsync(query, parsed.IsSeries, parsed.Year, cancellationToken).ConfigureAwait(false);
            if (LocalMediaName.UniqueExactMatch(parsed, candidates) is { } match) return match;
        }
        return null;
    }
}
