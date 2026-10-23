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
public sealed class DandanPlayClient : IDisposable
{
    public const string ApiOrigin = "https://api.dandanplay.net";
    public const string DeveloperUrl = "https://dev.dandanplay.com";
    public const string WebsiteUrl = "https://www.dandanplay.com";
    public const int MaximumResponseBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(2);
    private readonly AppSettings _settings;
    private readonly DandanPlayCredentials _credentials;
    private readonly string _cacheDirectory;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _requests = new(1, 1);

    public DandanPlayClient(AppSettings settings, DandanPlayCredentials credentials, string cacheDirectory,
        HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings;
        _credentials = credentials;
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

    public async Task<IReadOnlyList<DandanPlayEpisode>> SearchAsync(string title, string? episode,
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
        var ids = new HashSet<long>();
        foreach (var anime in animes.EnumerateArray())
        {
            var animeTitle = Text(anime, "animeTitle");
            if (animeTitle.Length == 0 || !anime.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in episodes.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("episodeId", out var id)
                    || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 || !ids.Add(number)) continue;
                result.Add(new(number, animeTitle, Text(entry, "episodeTitle")));
                if (result.Count == 1000) return result;
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<DanmakuComment>> GetCommentsAsync(long episodeId, bool withRelated,
        CancellationToken cancellationToken = default)
    {
        if (episodeId <= 0) throw new DandanPlayException("弹幕库编号无效。");
        var path = $"/api/v2/comment/{episodeId.ToString(CultureInfo.InvariantCulture)}?withRelated={withRelated.ToString().ToLowerInvariant()}&chConvert=0";
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
        if (!_settings.Danmaku.Enabled) throw new DandanPlayException("请先在设置 → 弹幕中开启弹弹play。");
        var appId = _settings.Danmaku.AppId;
        var secret = _credentials.GetSecret(_settings.Danmaku);
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 200 || appId.Any(char.IsControl) || secret.Length == 0)
            throw new DandanPlayException("请在设置 → 弹幕中填写有效的 AppId 和 AppSecret；复制到其他 Windows 账号的密钥需要重新输入。");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var entered = false;
        var protectedSecret = _settings.Danmaku.ProtectedAppSecret;
        try
        {
            await _requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            // 应用弹幕是私有库；缓存按应用与凭据隔离，换应用不能沿用上一份数据或成功状态。
            var cacheKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appId + "\n"
                + protectedSecret + "\n" + path)));
            var cache = Path.Combine(_cacheDirectory, cacheKey + ".json");
            if (TryReadCache(cache) is { } cached)
            {
                try { return Parse(cached, comments); }
                catch (DandanPlayException) { /* 损坏缓存不作为联网失败，重取这一份。 */ }
            }

            var uri = new Uri(ApiOrigin + path);
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/json");
                if (uri.IdnHost.Equals("api.dandanplay.net", StringComparison.OrdinalIgnoreCase))
                {
                    var timestamp = _clock().ToUnixTimeSeconds();
                    request.Headers.Add("X-AppId", appId);
                    request.Headers.Add("X-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
                    request.Headers.Add("X-Signature", Signature(appId, secret, timestamp, uri.AbsolutePath));
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
                if (!response.IsSuccessStatusCode) throw new DandanPlayException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "弹弹play 鉴权失败，请检查 AppId、AppSecret、应用授权和系统时间。",
                    HttpStatusCode.TooManyRequests => "弹弹play 请求额度或频率受限，请稍后重试。",
                    _ => $"弹弹play 请求失败（HTTP {(int)response.StatusCode}），请稍后重试。",
                });
                var bytes = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
                var parsed = Parse(bytes, comments);
                TryWriteCache(cache, bytes);
                return parsed;
            }
            throw new DandanPlayException("弹幕请求跳转过多。");
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
