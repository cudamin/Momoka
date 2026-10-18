using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Momoka.Plex;

/// <summary>
/// Who is asking, and how. Built at each call site from either the live connection or the device
/// identity, so nothing here has to be stored on the transport and two accounts can be served by one
/// instance without racing.
/// </summary>
/// <param name="AccessToken">
/// The <c>X-Plex-Token</c>. It travels in a header and only in a header: never a query string, never a
/// log line, never a launch argument — the same rule the Emby token is held to, for the same reason
/// (a token in a URL ends up in the server's own access log and in every proxy in between).
/// </param>
/// <param name="ClientIdentifier">
/// Plex's <c>X-Plex-Client-Identifier</c>. Stable across restarts, which is what stops the server
/// piling up one phantom player per launch in its Now Playing list.
/// </param>
public readonly record struct PlexCall(
    string? AccessToken = null,
    string? ClientIdentifier = null,
    string? ClientVersion = null,
    IReadOnlyList<KeyValuePair<string, string>>? Headers = null)
{
    public static PlexCall Anonymous => default;

    public static PlexCall For(string? accessToken, string? clientIdentifier, string? clientVersion) =>
        new(accessToken, clientIdentifier, clientVersion);
}

/// <summary>
/// The single place that performs HTTP for Plex.
/// <para>
/// Server requests stay on the server's own origin. The one request that deliberately leaves it is
/// <see cref="SignInToAccountAsync"/>, which talks to Plex's account service because that is where a
/// Plex account is authenticated — a Plex server does not verify an account password itself. That URI
/// is built here from a constant rather than handed in, so no caller can point it somewhere else, and
/// the account password goes in a JSON body rather than a query string.
/// </para>
/// </summary>
public sealed class PlexHttp : IDisposable
{
    /// <summary>Plex's account authority. Reached only by <see cref="SignInToAccountAsync"/>.</summary>
    public const string AccountAuthority = "https://plex.tv";

    public const string Product = "Momoka";

    private readonly HttpClient _http;

    public static JsonSerializerOptions Json { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public PlexHttp() : this(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    { }

    public PlexHttp(HttpMessageHandler handler, bool disposeHandler = true)
    {
        _http = new HttpClient(handler, disposeHandler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>A JSON response. Plex wraps everything the server answers in a <c>MediaContainer</c>.</summary>
    public async Task<JsonObject> GetJsonAsync(Uri uri, PlexCall call, CancellationToken cancellationToken = default)
    {
        var bytes = await SendAsync(HttpMethod.Get, uri, null, call, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(bytes) as JsonObject ?? throw new PlexApiException("Plex 返回了无法解析的响应");
        }
        catch (JsonException)
        {
            throw new PlexApiException("Plex 返回了无法解析的响应");
        }
    }

    /// <summary>
    /// A request whose answer is not read. Plex's scrobble and timeline endpoints answer with a body,
    /// but nothing in it is used, and a body this client cannot parse must not fail a report that the
    /// server has already acted on.
    /// </summary>
    public async Task GetAsync(Uri uri, PlexCall call, CancellationToken cancellationToken = default) =>
        _ = await SendAsync(HttpMethod.Get, uri, null, call, cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> GetBytesAsync(Uri uri, PlexCall call, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Get, uri, null, call, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Exchanges a Plex account's credentials for an access token. This is the only request in this
    /// class that leaves the server's origin; see the class comment.
    /// </summary>
    public async Task<JsonObject> SignInToAccountAsync(string username, string password, PlexCall call,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(call.ClientIdentifier))
            throw new PlexApiException("缺少 Plex 客户端标识，无法登录账号");
        var body = new JsonObject { ["login"] = username.Trim(), ["password"] = password };
        var payload = body.ToJsonString(Json);
        var bytes = await SendCoreAsync(HttpMethod.Post, new Uri(AccountAuthority + "/api/v2/users/signin"),
            Encoding.UTF8.GetBytes(payload), "application/json", call, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(bytes) as JsonObject ?? throw new PlexApiException("Plex 账号服务返回了无法解析的响应");
        }
        catch (JsonException)
        {
            throw new PlexApiException("Plex 账号服务返回了无法解析的响应");
        }
    }

    private async Task<byte[]> SendAsync(HttpMethod method, Uri uri, byte[]? body, PlexCall call,
        CancellationToken cancellationToken)
    {
        PlexEndpoint.Validate(uri);
        return await SendCoreAsync(method, uri, body, body is null ? null : "application/json", call, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<byte[]> SendCoreAsync(HttpMethod method, Uri uri, byte[]? body, string? contentType,
        PlexCall call, CancellationToken cancellationToken)
    {
        var origin = uri;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd(Product + "/1.0");
                request.Headers.TryAddWithoutValidation("X-Plex-Product", Product);
                request.Headers.TryAddWithoutValidation("X-Plex-Platform", "Windows");
                if (!string.IsNullOrEmpty(call.ClientVersion))
                    request.Headers.TryAddWithoutValidation("X-Plex-Version", call.ClientVersion);
                if (!string.IsNullOrEmpty(call.ClientIdentifier))
                    request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", call.ClientIdentifier);
                if (!string.IsNullOrEmpty(call.AccessToken))
                {
                    if (call.AccessToken.Any(char.IsControl)) throw new PlexApiException("Plex 登录凭据无效");
                    request.Headers.TryAddWithoutValidation("X-Plex-Token", call.AccessToken);
                }
                if (call.Headers is { } extra)
                    foreach (var header in extra) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (body is not null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/json");
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= 3 || response.Headers.Location is null)
                        throw new PlexApiException("Plex 服务器重定向无效", response.StatusCode);
                    var next = new Uri(uri, response.Headers.Location);
                    PlexEndpoint.Validate(next);
                    if (!PlexEndpoint.SameOrigin(origin, next))
                        throw new PlexApiException("Plex 服务器重定向到了不同来源，已停止发送登录信息", response.StatusCode);
                    if (method != HttpMethod.Get && (int)response.StatusCode is not (307 or 308))
                        throw new PlexApiException("Plex 服务器要求改变请求方式的重定向", response.StatusCode);
                    uri = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw PlexApiException.FromStatus(response.StatusCode);
                var bytes = await ReadLimitedAsync(response.Content, 64 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                return bytes;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new PlexApiException("连接 Plex 服务器超时"); }
        catch (HttpRequestException)
        { throw new PlexApiException("无法连接 Plex 服务器，请检查地址和网络"); }
        catch (IOException)
        { throw new PlexApiException("读取 Plex 响应失败"); }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new PlexApiException("Plex 响应超过大小限制");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > limit) throw new PlexApiException("Plex 响应超过大小限制");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
