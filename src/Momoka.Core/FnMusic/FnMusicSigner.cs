using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Momoka.FnMusic;

/// <summary>Official music 1.1.19 SDK uk/ok/sk/ck middleware; these are published application constants.</summary>
public static class FnMusicSigner
{
    private const string ApplicationSalt = "NDzZTVxnRKP8Z0jXg1VAMonaG8akvh";
    private const string ApplicationKey = "6D5602D4-A342-4799-A0F0-BB795E7167D0";

    public static string Create(HttpMethod method, Uri uri, string? body = null) => Create(method, uri, body,
        RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static string Create(HttpMethod method, Uri uri, string? body, string nonce, long timestampMilliseconds)
    {
        if (nonce.Length != 6 || nonce.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("签名随机数必须是六位数字", nameof(nonce));
        var timestamp = timestampMilliseconds.ToString(CultureInfo.InvariantCulture);
        var data = method == HttpMethod.Get ? Momoka.FnNas.FnNasSigner.CanonicalQuery(uri) : body ?? "";
        var input = string.Join('_', ApplicationSalt, uri.AbsolutePath, nonce, timestamp, Md5(data), ApplicationKey);
        return $"nonce={nonce}&timestamp={timestamp}&sign={Md5(input)}";
    }

    public static string HashPassword(string password) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    public static string DeviceId(string deviceId) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(deviceId)));
    private static string Md5(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
