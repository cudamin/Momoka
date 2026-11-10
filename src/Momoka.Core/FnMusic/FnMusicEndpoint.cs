using Momoka.Emby;

namespace Momoka.FnMusic;

/// <summary>The independently deployed /music application, including reverse-proxy mounts.</summary>
public static class FnMusicEndpoint
{
    public static Uri Normalize(string input)
    {
        if (!TryNormalize(input, out var address, out var error)) throw new ArgumentException(error, nameof(input));
        return address;
    }

    public static bool TryNormalize(string input, out Uri address, out string error)
    {
        address = null!;
        error = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0) { error = "请填写飞牛音乐服务器地址"; return false; }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0)
        { error = "飞牛音乐服务器地址格式不正确"; return false; }
        if (parsed.Scheme is not ("http" or "https")) { error = "服务器地址只支持 http 或 https"; return false; }
        if (parsed.UserInfo.Length != 0) { error = "请在账户输入框填写凭据，不要写在服务器地址中"; return false; }
        var parts = parsed.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var mount = Array.FindLastIndex(parts, part => part.Equals("music", StringComparison.Ordinal));
        var path = mount >= 0 ? string.Join('/', parts.Take(mount + 1)) : string.Join('/', parts.Append("music"));
        address = new UriBuilder(parsed.Scheme, parsed.Host, parsed.Port) { Path = "/" + path + "/", Query = "", Fragment = "" }.Uri;
        return true;
    }

    public static Uri Api(Uri mount, string path, params (string Key, string? Value)[] query)
    {
        EmbyHttpRedirect.Validate(mount);
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains('?') || path.Contains('#')
            || Uri.TryCreate(path, UriKind.Absolute, out _) || path.Split('/').Any(part => part is "." or ".."))
            throw new ArgumentException("飞牛音乐接口路径无效", nameof(path));
        return EmbyUrl.Combine(new Uri(mount, "api/v1/"), path, query);
    }
}
