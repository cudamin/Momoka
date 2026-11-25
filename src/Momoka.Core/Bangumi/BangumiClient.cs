using System.Globalization;
using System.Net;
using System.Text.Json;
using Momoka.Infrastructure;

namespace Momoka.Bangumi;

/// <summary>按条目编号或核验后的作品标题匿名读取 Bangumi 官方评分。</summary>
public sealed partial class BangumiClient : IDisposable
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private readonly Lock _connectionLock = new();
    private readonly Func<string?>? _readProxyServer;
    private Connection? _connection;

    public BangumiClient(HttpMessageHandler? handler = null, Func<string?>? readProxyServer = null)
    {
        _readProxyServer = handler is null ? readProxyServer : null;
        _connection = new(CreateHttp(handler ?? CreateHandler(null)), null);
    }

    private static HttpClient CreateHttp(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Momoka/{AppIdentity.Version} (https://github.com/cudamin/Momoka)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    private static SocketsHttpHandler CreateHandler(IWebProxy? proxy) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        Proxy = proxy,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(8),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    public async Task<decimal?> RatingAsync(long subjectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (subjectId is < 1 or > 10000000) throw new ArgumentOutOfRangeException(nameof(subjectId));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var connection = AcquireConnection();
        try
        {
            var uri = new Uri("https://api.bgm.tv/v0/subjects/" + subjectId.ToString(CultureInfo.InvariantCulture));
            for (var attempt = 0; attempt < 4; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await connection.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);
                // bangumi/server 522cc1d, web/handler/subject/get.go redirects merged subjects to their canonical ID.
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (response.Headers.Location is not { } location) return null;
                    var target = new Uri(uri, location);
                    if (!TrySubject(target, out _)) return null;
                    uri = target;
                    continue;
                }
                if (!response.IsSuccessStatusCode || !TrySubject(uri, out var returnedId)) return null;
                using var document = await ReadDocumentAsync(response, deadline.Token).ConfigureAwait(false);
                if (document is null) return null;
                var root = document.RootElement;
                // Official DTO web/res/subject.go exposes the actual vote count separately from the score.
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var actualId) || actualId != returnedId
                    || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Number
                    || !type.TryGetInt32(out var kind) || kind is not (2 or 6)
                    || !root.TryGetProperty("rating", out var rating) || rating.ValueKind != JsonValueKind.Object
                    || !rating.TryGetProperty("total", out var total) || total.ValueKind != JsonValueKind.Number
                    || !total.TryGetInt64(out var votes) || votes <= 0
                    || !rating.TryGetProperty("score", out var score) || score.ValueKind != JsonValueKind.Number
                    || !score.TryGetDecimal(out var value)) return null;
                cancellationToken.ThrowIfCancellationRequested();
                return value is > 0 and <= 10 ? value : null;
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UriFormatException) { return null; }
        finally { ReleaseConnection(connection); }
    }

    private static async Task<JsonDocument?> ReadDocumentAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) return null;
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > MaximumResponseBytes) return null;
            body.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
    }

    private static bool TrySubject(Uri uri, out long subject)
    {
        const string prefix = "/v0/subjects/";
        subject = 0;
        return uri.Scheme == Uri.UriSchemeHttps && uri.Host == "api.bgm.tv" && uri.IsDefaultPort
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(uri.AbsolutePath[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out subject)
            && subject is >= 1 and <= 10000000;
    }

    private Connection AcquireConnection()
    {
        lock (_connectionLock)
        {
            var current = _connection ?? throw new ObjectDisposedException(nameof(BangumiClient));
            var proxy = _readProxyServer?.Invoke();
            if (current.Proxy != proxy)
            {
                _connection = new(CreateHttp(CreateHandler(ProxyEndpoint.CreateWebProxy(proxy))), proxy);
                if (current.Active == 0) current.Http.Dispose();
                current = _connection;
            }
            current.Active++;
            return current;
        }
    }

    private void ReleaseConnection(Connection connection)
    {
        lock (_connectionLock)
            if (--connection.Active == 0 && connection != _connection) connection.Http.Dispose();
    }

    public void Dispose()
    {
        lock (_connectionLock)
        {
            var current = _connection;
            _connection = null;
            if (current?.Active == 0) current.Http.Dispose();
        }
    }

    private sealed class Connection(HttpClient http, string? proxy)
    {
        internal HttpClient Http { get; } = http;
        internal string? Proxy { get; } = proxy;
        internal int Active { get; set; }
    }
}
