using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;

namespace Momoka.Danmaku;

/// <summary>一次解析成功的内置凭据。KeyId 只用于缓存隔离与诊断，不是密钥。</summary>
public sealed record DandanPlayCredential(string AppId, string Secret, string KeyId);

/// <summary>
/// 从 Momoka 自己的端点取回弹弹play 应用凭据：HTTPS、AES-GCM、DPAPI 落盘缓存，到期前自动换新。
/// <para>
/// 加解密密钥随客户端分发，所以这是「不把密钥写进公开源码/安装包字符串」而不是真正的保密；拿到客户端的人
/// 仍可解出内容。端点失败时若缓存未到期就继续用，避免一次网络抖动打断播放；401/403 由客户端调用
/// <see cref="Invalidate"/> 后重取一次。
/// </para>
/// </summary>
public sealed class DandanPlayBuiltInCredentialSource : IDisposable
{
    private readonly ISecretProtector _protector;
    private readonly string _cacheFile;
    private readonly string _endpoint;
    private readonly byte[] _key;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DandanPlayCredential? _cached;
    private DateTimeOffset _expires;
    private DateTimeOffset _refreshAt;

    public DandanPlayBuiltInCredentialSource(
        ISecretProtector protector,
        string cacheFile,
        string? endpoint = null,
        string? keyBase64 = null,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? clock = null)
    {
        _protector = protector;
        _cacheFile = cacheFile;
        _endpoint = endpoint ?? DandanPlayBuiltIn.Endpoint;
        _key = Convert.FromBase64String(keyBase64 ?? DandanPlayBuiltIn.Key);
        if (_key.Length != 32) throw new ArgumentException("弹幕凭据密钥必须是 32 字节。", nameof(keyBase64));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<DandanPlayCredential> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock();
            if (_cached is not null && now < _refreshAt) return _cached;

            var stored = TryReadCache();
            if (stored is not null)
            {
                _cached = stored.Value.Credential;
                _expires = stored.Value.Expires;
                _refreshAt = stored.Value.RefreshAt;
                if (now < _refreshAt) return _cached;
            }

            try
            {
                var fetched = await FetchAsync(cancellationToken).ConfigureAwait(false);
                _cached = fetched.Credential;
                _expires = fetched.Expires;
                _refreshAt = fetched.RefreshAt;
                TryWriteCache(fetched);
                return fetched.Credential;
            }
            catch (DandanPlayException) when (_cached is not null && now < _expires)
            {
                // 端点暂时不可用，手上这份还没到期：继续用，别让一次网络抖动打断播放。
                return _cached;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>丢掉内存与磁盘缓存；下一次 <see cref="GetAsync"/> 会重新向端点要一份。</summary>
    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _cached = null;
            _expires = default;
            _refreshAt = default;
            try { File.Delete(_cacheFile); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        finally { _gate.Release(); }
    }

    private static DateTimeOffset RefreshAt(DateTimeOffset expires, DateTimeOffset now)
    {
        var remaining = expires - now;
        var before = remaining > DandanPlayBuiltIn.RefreshBefore
            ? DandanPlayBuiltIn.RefreshBefore
            : TimeSpan.FromTicks(Math.Max(remaining.Ticks / 2, TimeSpan.FromSeconds(1).Ticks));
        return expires - before;
    }

    private async Task<StoredCredential> FetchAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.UserInfo.Length > 0
            || endpoint.Host.Length == 0
            || endpoint.Query.Length > 0
            || endpoint.Fragment.Length > 0)
            throw new DandanPlayException("内置弹幕凭据地址无效，请联系 Momoka 更新。");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("Momoka/" + AppIdentity.Version);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new DandanPlayException(response.StatusCode switch
                {
                    HttpStatusCode.NotFound => "内置弹幕凭据服务未部署或地址有误（HTTP 404）。",
                    HttpStatusCode.TooManyRequests => "内置弹幕凭据服务请求过于频繁，请稍后重试。",
                    _ => "内置弹幕凭据服务暂时不可用，请稍后重试。",
                });
            var bytes = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
            return Parse(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DandanPlayException("获取内置弹幕凭据超时，请检查网络后重试。");
        }
        catch (HttpRequestException) { throw new DandanPlayException("无法连接内置弹幕凭据服务，请检查网络后重试。"); }
        catch (IOException) { throw new DandanPlayException("读取内置弹幕凭据失败，请检查网络后重试。"); }
    }

    private StoredCredential Parse(byte[] bytes)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { throw new DandanPlayException("内置弹幕凭据服务返回了无效数据。"); }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryInt32(root, "v", out var version) || version != 1
                || !TryString(root, "kid", out var kid) || kid.Length is 0 or > 64
                || !TryInt64(root, "exp", out var exp)
                || !TryString(root, "data", out var data))
                throw new DandanPlayException("内置弹幕凭据服务返回了无法识别的数据。");

            var now = _clock();
            var expires = FromUnix(exp);
            if (expires <= now + TimeSpan.FromMinutes(1))
                throw new DandanPlayException("内置弹幕凭据已过期，请稍后重试。");
            if (expires > now + TimeSpan.FromDays(30))
                throw new DandanPlayException("内置弹幕凭据有效期异常，请联系 Momoka 更新。");

            byte[] encrypted;
            try { encrypted = Convert.FromBase64String(data); }
            catch (FormatException) { throw new DandanPlayException("内置弹幕凭据服务返回了无效数据。"); }
            if (encrypted.Length < 12 + 16 + 1)
                throw new DandanPlayException("内置弹幕凭据服务返回了无效数据。");

            byte[] plain;
            try
            {
                var nonce = encrypted.AsSpan(0, 12);
                var tag = encrypted.AsSpan(encrypted.Length - 16);
                var cipher = encrypted.AsSpan(12, encrypted.Length - 12 - 16);
                plain = new byte[cipher.Length];
                using var aes = new AesGcm(_key, 16);
                aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(Aad(kid, exp)));
            }
            catch (CryptographicException) { throw new DandanPlayException("内置弹幕凭据无法解密，请联系 Momoka 更新。"); }

            using var payload = ParsePayload(plain);
            if (payload.RootElement.ValueKind != JsonValueKind.Object
                || !TryString(payload.RootElement, "appId", out var appId)
                || !TryString(payload.RootElement, "appSecret", out var secret)
                || !TryInt64(payload.RootElement, "exp", out var payloadExp) || payloadExp != exp
                || !Valid(appId, secret, kid))
                throw new DandanPlayException("内置弹幕凭据内容无效，请联系 Momoka 更新。");

            return new(new DandanPlayCredential(appId, secret, kid), expires, RefreshAt(expires, now));
        }
    }

    private static JsonDocument ParsePayload(byte[] plain)
    {
        try { return JsonDocument.Parse(plain); }
        catch (JsonException) { throw new DandanPlayException("内置弹幕凭据内容无效，请联系 Momoka 更新。"); }
    }

    private StoredCredential? TryReadCache()
    {
        try
        {
            var info = new FileInfo(_cacheFile);
            if (!info.Exists || info.Length > DandanPlayBuiltIn.MaximumResponseBytes) return null;
            var plain = _protector.Unprotect(File.ReadAllText(_cacheFile));
            using var document = JsonDocument.Parse(plain);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, "kid", out var kid) || kid.Length is 0 or > 64
                || !TryInt64(root, "exp", out var exp)
                || !TryInt64(root, "refreshAt", out var refreshAt)
                || !TryString(root, "appId", out var appId)
                || !TryString(root, "appSecret", out var secret)
                || !Valid(appId, secret, kid)) return null;
            var expires = FromUnix(exp);
            if (expires <= _clock()) return null;
            var refresh = FromUnix(refreshAt);
            return new(new DandanPlayCredential(appId, secret, kid), expires, refresh > expires ? expires : refresh);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException
            or ArgumentException or FormatException or JsonException or ArgumentOutOfRangeException
            or DandanPlayException)
        {
            return null;
        }
    }

    private void TryWriteCache(StoredCredential stored)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var json = JsonSerializer.Serialize(new
            {
                v = 1,
                kid = stored.Credential.KeyId,
                exp = stored.Expires.ToUnixTimeSeconds(),
                refreshAt = stored.RefreshAt.ToUnixTimeSeconds(),
                appId = stored.Credential.AppId,
                appSecret = stored.Credential.Secret
            });
            var protectedText = _protector.Protect(json);
            var temporary = _cacheFile + ".tmp";
            File.WriteAllText(temporary, protectedText, new UTF8Encoding(false));
            File.Move(temporary, _cacheFile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException) { }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > DandanPlayBuiltIn.MaximumResponseBytes)
            throw new DandanPlayException("内置弹幕凭据响应过大，已停止下载。");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (memory.Length + read > DandanPlayBuiltIn.MaximumResponseBytes)
                throw new DandanPlayException("内置弹幕凭据响应过大，已停止下载。");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static string Aad(string kid, long exp) =>
        "momoka-danmaku-credential-v1|" + kid + "|" + exp.ToString(CultureInfo.InvariantCulture);

    private static bool Valid(string appId, string secret, string kid) =>
        appId.Length is > 0 and <= 200 && secret.Length is > 0 and <= 512 && kid.Length is > 0 and <= 64
        && !appId.Any(char.IsControl) && !secret.Any(char.IsControl) && !kid.Any(char.IsControl);

    private static DateTimeOffset FromUnix(long value)
    {
        try { return DateTimeOffset.FromUnixTimeSeconds(value); }
        catch (ArgumentOutOfRangeException) { throw new DandanPlayException("内置弹幕凭据有效期无效，请联系 Momoka 更新。"); }
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? "";
        return true;
    }

    private static bool TryInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static bool TryInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private readonly record struct StoredCredential(
        DandanPlayCredential Credential, DateTimeOffset Expires, DateTimeOffset RefreshAt);

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
