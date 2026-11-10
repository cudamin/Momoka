using System.Security.Cryptography;
using Momoka.Configuration;

namespace Momoka.Danmaku;

public sealed record BilibiliAccount(long UserId, string Name);

/// <summary>B站登录凭据只在进程内使用；浏览器候选和磁盘密文都经过同一白名单。</summary>
public static class BilibiliSession
{
    public const string LoginUrl = "https://passport.bilibili.com/login";
    private static readonly string[] CookieNames = ["SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "buvid3", "buvid4"];

    public static string CookieHeader(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in cookies)
        {
            if (!CookieNames.Contains(name, StringComparer.Ordinal)) continue;
            if (string.IsNullOrEmpty(value) || value.Length > 4096
                || value.Any(character => character is < '!' or > '~' or ';' or ',' or '"' or '\\')
                || !accepted.TryAdd(name, value)) return "";
        }
        if (!accepted.ContainsKey("SESSDATA")) return "";
        return string.Join("; ", CookieNames.Where(accepted.ContainsKey).Select(name => name + "=" + accepted[name]));
    }

    public static bool IsOfficialNavigation(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0
        && (uri.IdnHost.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase));

    public static string NormalizeCookie(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie) || cookie.Length > 16384 || cookie.Any(char.IsControl)) return "";
        var parts = new List<KeyValuePair<string, string>>();
        foreach (var part in cookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0) return "";
            parts.Add(new(part[..equals], part[(equals + 1)..]));
        }
        return CookieHeader(parts);
    }

    public static string Protect(ISecretProtector protector, string cookie)
    {
        var normalized = NormalizeCookie(cookie);
        if (normalized.Length == 0) throw new CryptographicException("B站登录信息无效，请重新登录。");
        try
        {
            var encrypted = protector.Protect(normalized);
            if (string.IsNullOrWhiteSpace(encrypted) || encrypted.Length > 32768) throw new InvalidOperationException();
            return encrypted;
        }
        catch (Exception)
        {
            throw new CryptographicException("无法加密 B站登录信息，原账号未更改。");
        }
    }

    public static string Unprotect(ISecretProtector? protector, string? protectedCookie, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(protectedCookie))
        {
            error = "请先在设置 → 弹幕中登录 B站。";
            return "";
        }
        try
        {
            var cookie = protector is null ? "" : NormalizeCookie(protector.Unprotect(protectedCookie));
            if (cookie.Length > 0) return cookie;
        }
        catch (Exception) { /* 保护器异常可能含凭据，不能作为 inner exception 或日志传出。 */ }
        error = "B站登录信息无法解密，请在设置 → 弹幕中重新登录。";
        return "";
    }
}
