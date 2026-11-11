using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnMusic;

/// <summary>Cookie identity is supplied per request; it cannot leak through a shared cookie container.</summary>
public sealed class FnMusicHttp : IDisposable
{
    private readonly HttpClient _http;
    public static JsonSerializerOptions Json { get; } = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public FnMusicHttp() : this(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(8), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { }

    public FnMusicHttp(HttpMessageHandler handler, bool disposeHandler = true)
    {
        if (handler is SocketsHttpHandler sockets) { sockets.AllowAutoRedirect = false; sockets.UseCookies = false; }
        if (handler is HttpClientHandler managed) { managed.AllowAutoRedirect = false; managed.UseCookies = false; }
        _http = new(handler, disposeHandler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<JsonNode?> SendAsync(HttpMethod method, Uri uri, JsonNode? body, string? token, CancellationToken ct) =>
        SendAndReadAsync(method, uri, body, token, false, ct, async (response, cancellation) =>
        {
            var bytes = await ReadLimitedAsync(response.Content, 32 * 1024 * 1024, cancellation).ConfigureAwait(false);
            JsonObject envelope;
            try { envelope = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException(); }
            catch (JsonException) { throw new EmbyApiException("飞牛音乐返回了无法解析的响应", response.StatusCode); }
            if (envelope["code"] is not JsonValue value || !TryCode(value, out var code))
                throw new EmbyApiException("飞牛音乐响应缺少状态码", response.StatusCode);
            if (code is not (0 or 200)) throw Business(code);
            return envelope["data"]?.DeepClone();
        });

    public Task<byte[]> GetImageAsync(Uri uri, string token, CancellationToken ct) =>
        SendAndReadAsync(HttpMethod.Get, uri, null, token, true, ct, async (response, cancellation) =>
        {
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                throw new EmbyApiException("飞牛音乐未返回有效封面", response.StatusCode);
            return await ReadLimitedAsync(response.Content, 32 * 1024 * 1024, cancellation).ConfigureAwait(false);
        });

    private async Task<T> SendAndReadAsync<T>(HttpMethod method, Uri uri, JsonNode? body, string? token, bool image,
        CancellationToken ct, Func<HttpResponseMessage, CancellationToken, Task<T>> read)
    {
        EmbyHttpRedirect.Validate(uri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(image ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30));
        var origin = uri;
        var payload = body?.ToJsonString(Json);
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(image ? "image/*" : "application/json"));
                if (token is not null)
                {
                    if (token.Length == 0 || token.Any(char.IsControl)) throw new EmbyTokenExpiredException("飞牛音乐登录凭据无效");
                    request.Headers.TryAddWithoutValidation("Cookie", "music-token=" + Uri.EscapeDataString(token));
                }
                if (!image) request.Headers.TryAddWithoutValidation("authx", FnMusicSigner.Create(method, uri, payload));
                if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= 3 || response.Headers.Location is null) throw new EmbyApiException("飞牛音乐服务器重定向无效", response.StatusCode);
                    var next = new Uri(uri, response.Headers.Location);
                    EmbyHttpRedirect.Validate(next);
                    if (!EmbyHttpRedirect.SameOrigin(origin, next)) throw new EmbyApiException("飞牛音乐重定向到了不同来源，已停止发送登录信息", response.StatusCode);
                    if (method != HttpMethod.Get && (int)response.StatusCode is not (307 or 308)) throw new EmbyApiException("飞牛音乐服务器要求改变请求方式", response.StatusCode);
                    uri = next;
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new EmbyTokenExpiredException();
                if (!response.IsSuccessStatusCode) throw new EmbyApiException($"飞牛音乐服务器返回 HTTP {(int)response.StatusCode}", response.StatusCode);
                var result = await read(response, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                return result;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new EmbyUnreachableException("连接飞牛音乐服务器超时"); }
        catch (HttpRequestException) { throw new EmbyUnreachableException("无法连接飞牛音乐服务器，请检查地址和网络"); }
        catch (IOException) { throw new EmbyUnreachableException("读取飞牛音乐响应失败"); }
    }

    private static bool TryCode(JsonValue value, out int code) => value.TryGetValue(out code)
        || value.TryGetValue<string>(out var text) && int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out code);

    private static EmbyApiException Business(int code) => code == 120001 ? new EmbyTokenExpiredException() : new EmbyApiException(code switch
    {
        100002 => "飞牛音乐拒绝了请求参数", 100003 => "该操作需要飞牛音乐管理员权限", 100004 => "当前飞牛音乐账户没有执行此操作的权限",
        100005 => "飞牛音乐上找不到该条目", 110002 => "请先在飞牛音乐网页端完成初始化", 120002 => "飞牛音乐账户已被禁用",
        _ => $"飞牛音乐操作失败（错误码 {code}）"
    });

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new EmbyApiException("飞牛音乐响应超过大小限制");
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > limit) throw new EmbyApiException("飞牛音乐响应超过大小限制");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
