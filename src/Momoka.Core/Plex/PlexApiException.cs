using System.Net;

namespace Momoka.Plex;

/// <summary>
/// Messages are locally generated on purpose: a Plex error body can echo the account name, and the
/// request that produced it carried the access token. Nothing the server wrote is ever surfaced.
/// </summary>
public sealed class PlexApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>
    /// A 401 from the server means the token is no longer good for it — either it was revoked, or the
    /// account lost access to this server. Either way the session has to re-authenticate, which is the
    /// same contract <c>EmbyTokenExpiredException</c> has.
    /// </summary>
    public bool IsTokenExpired => StatusCode == HttpStatusCode.Unauthorized;

    /// <summary>The account itself was rejected — a wrong password, or an account that may not sign in.</summary>
    public bool IsAuthenticationFailure => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>Translated by <see cref="PlexMediaAdapter.Translate"/> for the session's benefit.</summary>
    internal static PlexApiException FromStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new("Plex 拒绝了这次访问（令牌无效，或该账号无权访问这台服务器）", status),
        HttpStatusCode.Forbidden => new("当前 Plex 账号没有访问该内容的权限", status),
        HttpStatusCode.NotFound => new("Plex 上找不到该资源", status),
        _ => new($"Plex 服务器返回 HTTP {(int)status}", status)
    };
}
