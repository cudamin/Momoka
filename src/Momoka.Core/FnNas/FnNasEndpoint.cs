namespace Momoka.FnNas;

/// <summary>The media application's origin and /v mount, independent of the fnOS system API.</summary>
public static class FnNasEndpoint
{
    public static Uri Normalize(string input)
    {
        if (!TryNormalize(input, out var address, out var error))
            throw new ArgumentException(error, nameof(input));
        return address;
    }

    public static bool TryNormalize(string input, out Uri address, out string error)
    {
        address = null!;
        error = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0) { error = "请填写飞牛影视服务器地址"; return false; }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
        { error = "飞牛影视服务器地址格式不正确"; return false; }
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        { error = "服务器地址只支持 http 或 https"; return false; }
        if (parsed.UserInfo.Length != 0)
        { error = "请在账户输入框填写凭据，不要写在服务器地址中"; return false; }

        var parts = parsed.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var mount = Array.FindLastIndex(parts, part => part.Equals("v", StringComparison.Ordinal));
        var path = mount >= 0 ? string.Join('/', parts.Take(mount + 1)) : string.Join('/', parts.Append("v"));
        address = new UriBuilder(parsed.Scheme, parsed.Host, parsed.Port)
        { Path = "/" + path + "/", Query = "", Fragment = "" }.Uri;
        return true;
    }

    public static string ToDisplayString(Uri apiBase) => apiBase.GetLeftPart(UriPartial.Path).TrimEnd('/');

    public static Uri Api(Uri apiBase, string relativePath)
    {
        Validate(apiBase);
        if (relativePath.StartsWith('/') || relativePath.Contains('\\') || relativePath.Contains('#') ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _) ||
            relativePath.Split('?', 2)[0].Split('/').Any(part => part is "." or ".."))
            throw new ArgumentException("飞牛影视接口路径无效", nameof(relativePath));
        var result = new Uri(apiBase, relativePath);
        if (!SameOrigin(apiBase, result) || !result.AbsolutePath.StartsWith(apiBase.AbsolutePath, StringComparison.Ordinal))
            throw new ArgumentException("飞牛影视接口不能离开当前服务器", nameof(relativePath));
        return result;
    }

    public static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(['/', '\\', '?', '#', '%']) >= 0 || value.Any(char.IsControl))
            throw new ArgumentException("飞牛影视条目标识无效", nameof(value));
        return Uri.EscapeDataString(value);
    }

    internal static void Validate(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("飞牛影视请求地址无效", nameof(uri));
    }

    internal static bool SameOrigin(Uri first, Uri second) =>
        first.Scheme.Equals(second.Scheme, StringComparison.OrdinalIgnoreCase) &&
        first.Host.Equals(second.Host, StringComparison.OrdinalIgnoreCase) && first.Port == second.Port;
}
