namespace Momoka.Danmaku;

/// <summary>验证自部署地址，并保留反向代理的路径前缀。</summary>
public static class DanmuApiEndpoint
{
    public static bool TryCreate(string? address, out Uri baseUri, out string error)
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

        var path = parsed.AbsolutePath;
        if (path.Length == 0) path = "/";
        if (!path.EndsWith('/')) path += "/";
        baseUri = new UriBuilder(parsed) { Path = path, Query = "", Fragment = "" }.Uri;
        return true;
    }
}
