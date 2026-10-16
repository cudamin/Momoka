using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Momoka.FnNas;

/// <summary>Verified against the official 0.9.8 frontend's request middleware (mediasrv 0.8.43).</summary>
public static class FnNasSigner
{
    // Published application identity, not an account credential. Never include it in diagnostics.
    private const string ApplicationSecret = "NDzZTVxnRKP8Z0jXg1VAMonaG8akvh";
    private const string ApplicationKey = "16CCEB3D-AB42-077D-36A1-F355324E4237";

    public static string Create(HttpMethod method, Uri uri, string? jsonBody = null) =>
        Create(method, uri, jsonBody, RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static string Create(HttpMethod method, Uri uri, string? jsonBody, string nonce, long timestampMilliseconds)
    {
        FnNasEndpoint.Validate(uri);
        if (nonce.Length != 6 || nonce.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("签名随机数必须是六位数字", nameof(nonce));
        var timestamp = timestampMilliseconds.ToString(CultureInfo.InvariantCulture);
        var data = method == HttpMethod.Get ? CanonicalQuery(uri) : jsonBody ?? "";
        var input = string.Join('_', ApplicationSecret, uri.AbsolutePath, nonce, timestamp, Md5(data), ApplicationKey);
        return $"nonce={nonce}&timestamp={timestamp}&sign={Md5(input)}";
    }

    /// <summary>URLSearchParams → last duplicate wins → ordinal key sort → decoded key=value pairs.</summary>
    public static string CanonicalQuery(Uri uri)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (value is not ("undefined" or "null")) values[key] = value;
        }
        return string.Join('&', values.Select(pair => pair.Key + "=" + pair.Value));
    }

    public static string HashPassword(string password) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    private static string Md5(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
