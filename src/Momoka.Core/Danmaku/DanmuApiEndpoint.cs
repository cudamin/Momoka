namespace Momoka.Danmaku;

/// <summary>用户自部署的 danmu_api 服务地址与可选令牌拼成的请求基址。</summary>
public static class DanmuApiEndpoint
{
    public static bool TryCreate(string? address, string? token, out Uri baseUri, out string error)
    {
        baseUri = null!;
        error = "";

        var raw = (address ?? "").Trim();
        if (raw.Length == 0)
        {
            error = "请先填写 danmu_api 服务地址。";
            return false;
        }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || parsed.UserInfo.Length > 0
            || parsed.Host.Length == 0
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0)
        {
            error = "danmu_api 地址必须是 http/https 的完整地址，不能带账号、查询或片段。";
            return false;
        }

        var cleanToken = (token ?? "").Trim();
        if (cleanToken.Any(char.IsControl))
        {
            error = "danmu_api 令牌不能包含控制字符。";
            return false;
        }
        if (cleanToken.Length > 512)
        {
            error = "danmu_api 令牌过长。";
            return false;
        }

        var path = parsed.AbsolutePath;
        if (path.Length == 0) path = "/";
        if (!path.EndsWith('/')) path += "/";
        if (cleanToken.Length > 0) path += Uri.EscapeDataString(cleanToken) + "/";
        baseUri = new UriBuilder(parsed) { Path = path, Query = "", Fragment = "" }.Uri;
        return true;
    }
}
