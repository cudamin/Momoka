using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Momoka.FnNas;

public sealed class FnNasHttp : IDisposable
{
    private readonly HttpClient _http;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);
    public static JsonSerializerOptions Json { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public FnNasHttp() : this(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    { }

    public FnNasHttp(HttpMessageHandler handler, bool disposeHandler = true)
    {
        _http = new HttpClient(handler, disposeHandler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<JsonNode?> SendAsync(HttpMethod method, Uri uri, JsonNode? body, string? accessToken,
        CancellationToken cancellationToken = default) =>
        SendAndReadAsync(method, uri, body, accessToken, ReadEnvelopeAsync, cancellationToken);

    internal Task<JsonNode?> UploadAsync(Uri uri, byte[] bytes, string filename, string? accessToken, CancellationToken cancellationToken) =>
        SendAndReadAsync(HttpMethod.Post, uri, null, accessToken, ReadEnvelopeAsync, cancellationToken, () =>
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", filename);
            return form;
        }, sign: false, timeout: TransferTimeout);

    public Task<byte[]> GetBytesAsync(Uri uri, string? accessToken, CancellationToken cancellationToken = default) =>
        SendAndReadAsync(HttpMethod.Get, uri, null, accessToken,
            (response, token) => ReadLimitedAsync(response.Content, 64 * 1024 * 1024, token), cancellationToken,
            timeout: TransferTimeout);

    private async Task<T> SendAndReadAsync<T>(HttpMethod method, Uri uri, JsonNode? body, string? accessToken,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken cancellationToken,
        Func<HttpContent>? createContent = null, bool sign = true, TimeSpan? timeout = null)
    {
        FnNasEndpoint.Validate(uri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? RequestTimeout);
        var payload = body?.ToJsonString(Json);
        var origin = uri;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd("Momoka/1.0");
                if (!string.IsNullOrEmpty(accessToken))
                {
                    if (accessToken.Any(char.IsControl)) throw new FnNasApiException("飞牛影视登录凭据无效");
                    request.Headers.TryAddWithoutValidation("Authorization", accessToken);
                }
                if (sign) request.Headers.TryAddWithoutValidation("authx", FnNasSigner.Create(method, uri, payload));
                if (createContent is not null) request.Content = createContent();
                else if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= 3 || response.Headers.Location is null)
                        throw new FnNasApiException("飞牛影视服务器重定向无效", statusCode: response.StatusCode);
                    var next = new Uri(uri, response.Headers.Location);
                    FnNasEndpoint.Validate(next);
                    if (!FnNasEndpoint.SameOrigin(origin, next))
                        throw new FnNasApiException("飞牛影视服务器重定向到了不同来源，已停止发送登录信息", statusCode: response.StatusCode);
                    if (method != HttpMethod.Get && (int)response.StatusCode is not (307 or 308))
                        throw new FnNasApiException("飞牛影视服务器要求改变请求方式的重定向", statusCode: response.StatusCode);
                    uri = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new FnNasApiException($"飞牛影视服务器返回 HTTP {(int)response.StatusCode}", statusCode: response.StatusCode);
                var result = await read(response, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                return result;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new FnNasApiException("连接飞牛影视服务器超时", isTransportFailure: true); }
        catch (HttpRequestException)
        { throw new FnNasApiException("无法连接飞牛影视服务器，请检查地址和网络", isTransportFailure: true); }
        catch (IOException)
        { throw new FnNasApiException("读取飞牛影视响应失败", isTransportFailure: true); }
    }

    private static async Task<JsonNode?> ReadEnvelopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await ReadLimitedAsync(response.Content, 32 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        JsonObject envelope;
        try { envelope = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new FnNasApiException("飞牛影视返回了无法解析的响应", statusCode: response.StatusCode); }
        if (envelope["code"] is not JsonValue codeNode || !codeNode.TryGetValue<int>(out var code))
            throw new FnNasApiException("飞牛影视响应缺少状态码", statusCode: response.StatusCode);
        if (code != 0) throw FnNasApiException.Business(code);
        return envelope["data"]?.DeepClone();
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new FnNasApiException("飞牛影视响应超过大小限制");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > limit) throw new FnNasApiException("飞牛影视响应超过大小限制");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
