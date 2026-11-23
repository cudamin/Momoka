using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Momoka.Infrastructure;

namespace Momoka.AniBt;

/// <summary>
/// AniBT 的匿名查询。客户端不接收 MP / Emby 会话，也不下载种子；资源仅返回固定公开站点的地址。
/// 协议以 https://wiki.anibt.net/openapi.yaml 为准。
/// </summary>
public sealed partial class AniBtClient : IDisposable
{
    private static readonly Uri Origin = new("https://anibt.net/");
    private static readonly XNamespace RssNamespace = "https://anibt.net/xmlns/rss/1.0/";
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private readonly Lock _connectionLock = new();
    private readonly Func<string?>? _readProxyServer;
    private Connection? _connection;

    public AniBtClient(HttpMessageHandler? handler = null, Func<string?>? readProxyServer = null)
    {
        _readProxyServer = handler is null ? readProxyServer : null;
        _connection = new Connection(CreateHttpClient(handler ?? CreateHandler(null)), null);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    private static SocketsHttpHandler CreateHandler(IWebProxy? proxy) =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            Proxy = proxy,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

    private Connection AcquireConnection()
    {
        lock (_connectionLock)
        {
            var connection = _connection ?? throw new ObjectDisposedException(nameof(AniBtClient));
            var proxyServer = _readProxyServer?.Invoke();
            if (connection.ProxyServer != proxyServer)
            {
                // The handler keeps its proxy and pool for the life of the connection. Retire the
                // old client after its last request has finished reading the response body.
                var proxy = ProxyEndpoint.CreateWebProxy(proxyServer);
                _connection = new Connection(CreateHttpClient(CreateHandler(proxy)), proxyServer);
                if (connection.ActiveRequests == 0) connection.Http.Dispose();
                connection = _connection;
            }

            connection.ActiveRequests++;
            return connection;
        }
    }

    private void ReleaseConnection(Connection connection)
    {
        lock (_connectionLock)
        {
            if (--connection.ActiveRequests == 0 && connection != _connection) connection.Http.Dispose();
        }
    }

    private sealed class Connection(HttpClient http, string? proxyServer)
    {
        public HttpClient Http { get; } = http;
        public string? ProxyServer { get; } = proxyServer;
        public int ActiveRequests { get; set; }
    }

    public async Task<AniBtMatch> MatchAsync(string filename, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 上游只需要源文件名，不应该收到媒体服务器路径、查询参数或访问凭据。
        var title = filename?.Trim() ?? "";
        if (title.Contains("://", StringComparison.Ordinal))
            throw new AniBtException("请使用视频的源文件名识别");
        title = title.Replace('\\', '/');
        title = title[(title.LastIndexOf('/') + 1)..];
        ValidateText(title, 1000, "源文件名");
        var body = JsonSerializer.Serialize(new { rawTitle = title, limit = 10 });
        using var document = ParseJson(await RequestAsync("api/animes/torrent-match", body, false, cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        if (Number(root, "version") != 3 || Field(root, "decision").ValueKind != JsonValueKind.Object
            || Field(root, "candidates").ValueKind != JsonValueKind.Array)
            throw InvalidResponse();

        var candidates = new List<AniBtAnime>();
        foreach (var candidate in Field(root, "candidates").EnumerateArray())
        {
            var anime = ParseCandidate(candidate);
            if (anime is not null && !candidates.Any(existing => existing.AnimeId == anime.AnimeId && existing.BgmId == anime.BgmId))
                candidates.Add(anime);
        }

        var decision = Field(root, "decision");
        var kind = Text(decision, "kind");
        AniBtAnime? automatic = null;
        // 分数或候选排序不是自动采用的授权。manual 必须留给调用方选择；多作品/多季包同样不能自动改元数据。
        var contentKind = Text(Field(root, "content"), "kind");
        var supportedContent = contentKind is "anime_episode" or "anime_batch" or "anime_movie" or "anime_ova_oad" or "anime_special";
        if (kind == "auto" && supportedContent && Field(decision, "vetoes") is { ValueKind: JsonValueKind.Array } vetoes
            && vetoes.GetArrayLength() == 0)
        {
            var chosen = candidates.Where(candidate => candidate.AnimeId == Text(decision, "animeId")
                && candidate.BgmId == Number(decision, "bgmId")).ToArray();
            if (chosen.Length == 1) automatic = chosen[0];
        }

        // structure.season 是发布标题里的季号，不是 TMDB 季号；这里仅保留，调用方不可直接用于整理。
        return new AniBtMatch(candidates, automatic, kind, Text(decision, "confidence"), Text(decision, "reason"),
            NonnegativeInt(Field(root, "structure"), "season"));
    }

    public async Task<AniBtSearchPage> SearchAsync(string keyword, int page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = keyword?.Trim() ?? "";
        ValidateText(query, 100, "搜索词");
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        // 旧调用仍只读第一页；目录后续页必须使用 SearchCatalogAsync 返回的 cursor。
        if (page > 1) return new AniBtSearchPage([], false);
        return await SearchCatalogAsync(query, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AniBtRelease>> ReleasesAsync(AniBtAnime anime, CancellationToken cancellationToken)
        => (await ReleasePageAsync(anime, cancellationToken).ConfigureAwait(false)).Items;

    public async Task<AniBtReleasePage> ReleasePageAsync(AniBtAnime anime, CancellationToken cancellationToken, int? episode = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (anime.BgmId is <= 0 or > 10000000) throw new AniBtException("缺少有效的 Bangumi 编号");
        // RSS 的 episode 是独立的精确条件，不能把 MP 的季集串拼进 Bangumi 搜索词。
        // 默认 limit=100 按文档省略；公开 RSS 没有分页。
        var path = "rss/anime.xml?bgmId=" + anime.BgmId.ToString(CultureInfo.InvariantCulture)
            + (episode is { } number ? "&episode=" + number.ToString(CultureInfo.InvariantCulture) : "");
        var response = await RequestContentAsync(path, null, true, cancellationToken).ConfigureAwait(false);
        var canonicalId = QueryValue(response.Uri, "bgmId");
        if (!long.TryParse(canonicalId, NumberStyles.None, CultureInfo.InvariantCulture, out var selectedId)
            || selectedId is <= 0 or > 10000000) throw InvalidResponse();
        XDocument document;
        try
        {
            using var stream = new MemoryStream(response.Content);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumResponseBytes,
                IgnoreComments = true
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException error) { throw InvalidResponse(error); }
        if (document.Root?.Name != "rss" || document.Root.Element("channel") is not { } channel) throw InvalidResponse();

        var results = new List<AniBtRelease>();
        foreach (var item in channel.Elements("item"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var releaseId = ((string?)item.Element(RssNamespace + "releaseId") ?? (string?)item.Element("guid") ?? "").Trim();
            var title = ((string?)item.Element("title") ?? "").Trim();
            var identity = (string?)item.Element(RssNamespace + "bgmId");
            if (!ValidReleaseId(releaseId) || title.Length == 0 || results.Any(existing => existing.ReleaseId == releaseId)) continue;
            if (!long.TryParse(identity, NumberStyles.None, CultureInfo.InvariantCulture, out var bgmId) || bgmId != selectedId) continue;
            var download = new Uri(Origin, "api/torrent/" + releaseId + ".torrent");
            var enclosure = item.Element("enclosure");
            // 不将 feed 提供的任意地址交给 MP。只接受该发布的本站公开种子，不接受带凭据、查询或片段的 URL。
            if (!Uri.TryCreate((string?)enclosure?.Attribute("url"), UriKind.Absolute, out var supplied)
                || !SameOrigin(supplied) || supplied.AbsolutePath != download.AbsolutePath
                || supplied.Query.Length != 0 || supplied.Fragment.Length != 0) continue;
            var length = (string?)item.Element(RssNamespace + "fileSize") ?? (string?)enclosure?.Attribute("length");
            long? size = long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0 ? bytes : null;
            DateTimeOffset? published = DateTimeOffset.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
            results.Add(new AniBtRelease(releaseId, title, download, new Uri(Origin, "release/" + releaseId), size, published,
                (string?)item.Element(RssNamespace + "groupName") ?? "", (string?)item.Element(RssNamespace + "episodeKey") ?? "",
                (string?)item.Element(RssNamespace + "resolution") ?? ""));
        }
        return new AniBtReleasePage(selectedId, results);
    }

    private async Task<byte[]> RequestAsync(string path, string? body, bool xml, CancellationToken cancellationToken)
        => (await RequestContentAsync(path, body, xml, cancellationToken).ConfigureAwait(false)).Content;

    private async Task<(byte[] Content, Uri Uri)> RequestContentAsync(string path, string? body, bool xml, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var connection = AcquireConnection();
        try
        {
            var uri = new Uri(Origin, path);
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, uri);
                if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Accept.ParseAdd(xml ? "application/xml" : "application/json");
                using var response = await connection.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var next = response.Headers.Location is { } location ? new Uri(uri, location) : null;
                    if (body is not null || redirects >= 3 || next is null || !SameOrigin(next)
                        || next.AbsolutePath != uri.AbsolutePath || next.Fragment.Length != 0
                        || QueryValue(next, "episode") != QueryValue(uri, "episode")
                        || QueryValue(next, "episodeKey") != QueryValue(uri, "episodeKey")
                        || QueryValue(next, "season") != QueryValue(uri, "season"))
                        throw new AniBtException("AniBT 返回了无法使用的重定向", response.StatusCode);
                    uri = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var retry = response.Headers.RetryAfter?.Delta;
                    if (retry is null && response.Headers.RetryAfter?.Date is { } when) retry = when - DateTimeOffset.UtcNow;
                    if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
                    var message = response.StatusCode switch
                    {
                        HttpStatusCode.TooManyRequests => "AniBT 请求过于频繁，请稍后重试",
                        HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway => "AniBT 暂时无法提供结果，请稍后重试",
                        HttpStatusCode.NotFound => "AniBT 暂未收录此番剧",
                        _ => "AniBT 查询失败（HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "）"
                    };
                    throw new AniBtException(message, response.StatusCode, retry);
                }
                var type = response.Content.Headers.ContentType?.MediaType;
                if (type is not null && !(xml ? type is "application/xml" or "text/xml" or "application/rss+xml" : type == "application/json"))
                    throw InvalidResponse();
                return (await ReadBoundedContentAsync(response.Content, deadline.Token).ConfigureAwait(false), uri);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AniBtException("连接 AniBT 超时，请稍后重试", innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw new AniBtException("无法连接 AniBT，请检查网络后重试", innerException: error);
        }
        finally
        {
            ReleaseConnection(connection);
        }
    }

    private static async Task<byte[]> ReadBoundedContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes) throw new AniBtException("AniBT 返回的结果过大");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + count > MaximumResponseBytes) throw new AniBtException("AniBT 返回的结果过大");
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }

    private static AniBtAnime? ParseCandidate(JsonElement candidate)
    {
        var anime = Field(candidate, "anime");
        var animeId = Text(candidate, "animeId");
        var bgmId = Number(candidate, "bgmId");
        if (animeId.Length == 0 || bgmId is not (> 0 and <= 10000000) || Text(anime, "_id") != animeId
            || Number(anime, "bgmId") != bgmId) return null;
        var titles = Field(anime, "title");
        var title = FirstText(Text(titles, "chinese"), Text(titles, "chineseTraditional"), Text(titles, "native"),
            Text(titles, "romaji"), Text(titles, "english"), Text(anime, "titleRomaji"));
        if (title.Length == 0) return null;
        return new AniBtAnime(animeId, bgmId.Value, title,
            FirstText(Text(titles, "native"), Text(titles, "romaji"), Text(titles, "english")),
            ImageUrl(FirstText(Text(Field(anime, "coverImage"), "large"), Text(Field(anime, "coverImage"), "extraLarge"))),
            NonnegativeInt(anime, "seasonYear"), NonnegativeInt(anime, "episodes"), Text(anime, "format"));
    }

    private static void ValidateText(string value, int maximum, string label)
    {
        if (value.Length == 0 || value.Length > maximum || value.Any(char.IsControl))
            throw new AniBtException(label + "需为 1–" + maximum.ToString(CultureInfo.InvariantCulture) + " 个字符，且不能含控制字符");
    }

    private static JsonDocument ParseJson(byte[] content)
    {
        try { return JsonDocument.Parse(content); }
        catch (JsonException error) { throw InvalidResponse(error); }
    }

    private static JsonElement Field(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value : default;

    private static string Text(JsonElement item, string name) =>
        Field(item, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()?.Trim() ?? "" : "";

    private static long? Number(JsonElement item, string name) =>
        Field(item, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

    private static int? NonnegativeInt(JsonElement item, string name) =>
        Number(item, name) is >= 0 and <= int.MaxValue and var value ? (int)value : null;

    private static string FirstText(params string[] values) => values.FirstOrDefault(value => value.Length > 0) ?? "";

    private static string? ImageUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;

    private static bool SameOrigin(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Host == Origin.Host
        && uri.Port == Origin.Port && uri.UserInfo.Length == 0;

    private static string? QueryValue(Uri uri, string key) => uri.Query.TrimStart('?').Split('&')
        .Select(pair => pair.Split('=', 2)).Where(pair => pair[0] == key)
        .Select(pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "").FirstOrDefault();

    private static bool ValidReleaseId(string value) => value.StartsWith("rel_", StringComparison.Ordinal)
        && value.Length > 4 && value[4..].All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static AniBtException InvalidResponse(Exception? inner = null) =>
        new("AniBT 返回的数据无法读取，请稍后重试", innerException: inner);

    public void Dispose()
    {
        lock (_connectionLock)
        {
            _connection?.Http.Dispose();
            _connection = null;
        }
    }
}
