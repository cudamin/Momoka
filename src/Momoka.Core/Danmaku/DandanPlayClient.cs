using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;

namespace Momoka.Danmaku;

/// <summary>
/// 弹弹play 开放弹幕网络 v2。固定官方来源、签名鉴权、限长读取与两小时缓存；不访问媒体服务器或视频字节。
/// comment 接口的 302 只允许官方 HTTPS 子域，跨来源不传任何鉴权头。
/// </summary>
public sealed partial class DandanPlayClient : IDisposable
{
    public const string ApiOrigin = "https://api.dandanplay.net";
    public const string DeveloperUrl = "https://dev.dandanplay.com";
    public const string WebsiteUrl = "https://www.dandanplay.com";
    public const int MaximumResponseBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(2);
    private readonly AppSettings _settings;
    private readonly DandanPlayCredentials _credentials;
    private readonly DandanPlayBuiltInCredentialSource? _builtIn;
    private readonly string _cacheDirectory;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _requests = new(1, 1);

    public DandanPlayClient(AppSettings settings, DandanPlayCredentials credentials, string cacheDirectory,
        HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null,
        DandanPlayBuiltInCredentialSource? builtIn = null)
    {
        _settings = settings;
        _credentials = credentials;
        _builtIn = builtIn;
        _cacheDirectory = cacheDirectory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<DandanPlaySearchResult> SearchAsync(string title, string? episode,
        CancellationToken cancellationToken = default)
    {
        title = title.Trim();
        if (title.Length is < 2 or > 120) throw new DandanPlayException("请输入 2～120 个字符的作品名称。");
        if (episode is { Length: > 0 } && (episode.Length > 6 || !episode.All(char.IsAsciiDigit)
            || !int.TryParse(episode, out var episodeNumber) || episodeNumber <= 0))
            throw new DandanPlayException("集数必须是正整数，留空可搜索全部剧集。");
        var path = "/api/v2/search/episodes?anime=" + Uri.EscapeDataString(title) + "&v2=true";
        if (!string.IsNullOrEmpty(episode)) path += "&episode=" + Uri.EscapeDataString(episode);
        using var document = await GetAsync(path, comments: false, cancellationToken).ConfigureAwait(false);
        var result = new List<DandanPlayEpisode>();
        if (!document.RootElement.TryGetProperty("animes", out var animes) || animes.ValueKind != JsonValueKind.Array)
            throw new DandanPlayException("弹弹play 返回了无法识别的搜索结果。");
        var hasMore = document.RootElement.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True;
        var ids = new HashSet<long>();
        foreach (var anime in animes.EnumerateArray())
        {
            var animeTitle = Text(anime, "animeTitle");
            if (animeTitle.Length == 0 || !anime.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in episodes.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("episodeId", out var id)
                    || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 || !ids.Add(number)) continue;
                if (result.Count == 1000) return new(result, true);
                result.Add(new(number, animeTitle, Text(entry, "episodeTitle")));
            }
        }
        return new(result, hasMore);
    }

    public async Task<IReadOnlyList<DanmakuComment>> GetCommentsAsync(long episodeId, bool withRelated,
        CancellationToken cancellationToken = default)
    {
        if (episodeId <= 0) throw new DandanPlayException("弹幕库编号无效。");
        var path = CommentPath(episodeId, withRelated);
        using var document = await GetAsync(path, comments: true, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("comments", out var comments) || comments.ValueKind != JsonValueKind.Array)
            throw new DandanPlayException("弹弹play 返回了无法识别的弹幕数据。");
        var result = new List<DanmakuComment>();
        foreach (var entry in comments.EnumerateArray())
        {
            if (DanmakuComments.Parse(Text(entry, "p"), Text(entry, "m")) is { } parsed) result.Add(parsed);
            if (result.Count == DanmakuComments.MaximumCount) break;
        }
        return result.OrderBy(comment => comment.Time).ToArray();
    }

    public static string Signature(string appId, string secret, long timestamp, string path) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(appId
            + timestamp.ToString(CultureInfo.InvariantCulture) + path + secret)));

    public static bool SafeRedirect(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443
        && uri.UserInfo.Length == 0 && (uri.IdnHost.Equals("api.dandanplay.net", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".dandanplay.net", StringComparison.OrdinalIgnoreCase));

    private async Task<JsonDocument> GetAsync(string path, bool comments, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var entered = false;
        try
        {
            await _requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;

            // 内置凭据可能已被轮换：401/403 时丢掉缓存、换一份新凭据再试一次；自定义凭据不重试。
            for (var attempt = 0; ; attempt++)
            {
                var connection = await ResolveAsync(deadline.Token).ConfigureAwait(false);
                // 应用弹幕是私有库；缓存按应用与凭据隔离，换应用不能沿用上一份数据或成功状态。
                var cache = CachePath(connection.AppId, connection.Identity, path);
                if (TryReadCache(cache) is { } cached)
                {
                    try { return Parse(cached, comments); }
                    catch (DandanPlayException) { /* 损坏缓存不作为联网失败，重取这一份。 */ }
                }

                var uri = new Uri(ApiOrigin + path);
                var retryAuthentication = false;
                for (var redirects = 0; redirects <= 3; redirects++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Accept.ParseAdd("application/json");
                    if (uri.IdnHost.Equals("api.dandanplay.net", StringComparison.OrdinalIgnoreCase))
                    {
                        Sign(request, connection.AppId, connection.Secret);
                    }
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                    {
                        var location = response.Headers.Location;
                        if (!comments || location is null || redirects == 3) throw new DandanPlayException("弹弹play 返回了无效的跳转。");
                        uri = new Uri(uri, location);
                        if (!SafeRedirect(uri)) throw new DandanPlayException("已阻止弹幕请求跳转到非官方或不安全的地址。");
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        if (connection.BuiltIn is not null && attempt == 0
                            && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            _builtIn!.Invalidate();
                            retryAuthentication = true;
                            break;
                        }
                        throw new DandanPlayException(response.StatusCode switch
                        {
                            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when connection.BuiltIn is not null =>
                                "内置弹幕凭据鉴权失败，请稍后重试；如果持续失败，请在设置 → 弹幕 中改用你自己的应用凭据。",
                            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AuthenticationError(response),
                            HttpStatusCode.TooManyRequests => "弹弹play 请求额度或频率受限，请稍后重试。",
                            _ => $"弹弹play 请求失败（HTTP {(int)response.StatusCode}），请稍后重试。",
                        });
                    }
                    var bytes = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
                    var parsed = Parse(bytes, comments);
                    TryWriteCache(cache, bytes);
                    return parsed;
                }
                if (retryAuthentication) continue;
                throw new DandanPlayException("弹幕请求跳转过多。");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DandanPlayException("弹弹play 连接超时，请检查网络后重试。");
        }
        catch (HttpRequestException) { throw new DandanPlayException("无法连接弹弹play，请检查网络后重试。"); }
        catch (IOException) { throw new DandanPlayException("读取弹弹play 响应失败，请检查网络后重试。"); }
        catch (UriFormatException) { throw new DandanPlayException("弹弹play 返回了无效的跳转地址。"); }
        finally { if (entered) _requests.Release(); }
    }

    private sealed record Connection(string AppId, string Secret, string Identity, DandanPlayCredential? BuiltIn);

    private async Task<Connection> ResolveAsync(CancellationToken cancellationToken)
    {
        var danmaku = _settings.Danmaku;
        if (!danmaku.Enabled) throw new DandanPlayException("请先在设置 → 弹幕 中开启弹弹play。");

        if (danmaku.CredentialSource == DanmakuCredentialSource.Custom)
        {
            var appId = (danmaku.AppId ?? "").Trim();
            var protectedSecret = danmaku.ProtectedAppSecret ?? "";
            var secret = _credentials.GetSecret(danmaku);
            if (string.IsNullOrWhiteSpace(appId) || appId.Length > 200 || appId.Any(char.IsControl) || secret.Length == 0)
                throw new DandanPlayException("请在设置 → 弹幕 中填写有效的 AppId 和 AppSecret；复制到其他 Windows 账号的密钥需要重新输入。");
            return new(appId, secret, protectedSecret, null);
        }

        if (_builtIn is null)
            throw new DandanPlayException("内置弹幕凭据尚未配置，请在设置 → 弹幕 中改用你自己的应用凭据。");
        var credential = await _builtIn.GetAsync(cancellationToken).ConfigureAwait(false);
        var identity = "builtin:" + credential.KeyId + ":" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(credential.Secret)));
        return new(credential.AppId, credential.Secret, identity, credential);
    }

    private bool IsCurrent(Connection connection)
    {
        if (!_settings.Danmaku.Enabled) return false;
        if (connection.BuiltIn is not null) return _settings.Danmaku.CredentialSource != DanmakuCredentialSource.Custom;
        return _settings.Danmaku.CredentialSource == DanmakuCredentialSource.Custom
            && string.Equals(connection.AppId, (_settings.Danmaku.AppId ?? "").Trim(), StringComparison.Ordinal)
            && string.Equals(connection.Identity, _settings.Danmaku.ProtectedAppSecret ?? "", StringComparison.Ordinal);
    }

    private static string AuthenticationError(HttpResponseMessage response)
    {
        // 官方只定义这几个固定值；未知响应头和错误正文可能夹带凭据，绝不直接展示。
        var reason = response.Headers.TryGetValues("X-Error-Message", out var values) ? values.FirstOrDefault() : null;
        return reason switch
        {
            "Invalid Timestamp" => "弹弹play 鉴权失败：系统时间与服务器不一致，请同步 Windows 时间后重试。",
            "Invalid AppId" => "弹弹play 鉴权失败：AppId 或对应密钥无效，请核对开发者中心的应用凭据。",
            "Invalid Signature" or "Invalid AppSecret" => "弹弹play 鉴权失败：密钥或签名无效，请重新保存对应应用的 AppSecret。",
            "Missing Authentication Headers" => "弹弹play 鉴权失败：请求缺少应用认证头，请检查代理是否移除了请求头。",
            _ => "弹弹play 鉴权失败，请检查 AppId、AppSecret、应用授权和系统时间。",
        };
    }

    private static JsonDocument Parse(byte[] bytes, bool comments)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { throw new DandanPlayException("弹弹play 返回的数据不是有效的 JSON。"); }
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False
            || root.TryGetProperty("errorCode", out var code) && (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var number) || number != 0)
            || !root.TryGetProperty(comments ? "comments" : "animes", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            // 不回显服务器错误正文，避免代理把请求头或密钥夹回异常信息。
            throw new DandanPlayException("弹弹play 拒绝了请求或返回了无效数据，请检查应用授权后重试。");
        }
        return document;
    }

    private static string Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) throw new DandanPlayException("弹幕响应过大，已停止下载。");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (memory.Length + read > MaximumResponseBytes) throw new DandanPlayException("弹幕响应过大，已停止下载。");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private byte[]? TryReadCache(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var age = _clock().UtcDateTime - info.LastWriteTimeUtc;
            return info.Exists && info.Length <= MaximumResponseBytes && age >= TimeSpan.Zero && age < CacheLifetime
                ? File.ReadAllBytes(path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private void TryWriteCache(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            // 有界磁盘缓存：最多保留 16 份最近使用的结果，过期文件也在下一次写入时回收。
            foreach (var file in new DirectoryInfo(_cacheDirectory).EnumerateFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(15)) file.Delete();
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, _clock().UtcDateTime);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 缓存失败不影响本次播放。 */ }
    }

    public void Dispose()
    {
        _http.Dispose();
        _requests.Dispose();
    }
}

public sealed class DandanPlayException(string message) : Exception(message);
