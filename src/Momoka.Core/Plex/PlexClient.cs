using System.Text.Json.Nodes;

namespace Momoka.Plex;

/// <summary>
/// Everything needed to talk to one Plex server as one account.
/// <para>
/// The account identity is carried here rather than fetched per call because a Plex server does not
/// describe its users the way Emby does: there is no <c>/Users/{id}</c> to read, and the account a token
/// belongs to is decided by Plex's account service at sign-in. Holding it on the connection is what lets
/// the restore path check 「还是同一个人」 without a request of its own.
/// </para>
/// </summary>
public sealed record PlexConnection(
    Uri ApiBase,
    string AccessToken,
    string AccountId = "",
    string AccountName = "",
    string ClientIdentifier = "",
    string ClientVersion = "")
{
    /// <summary>Management scopes reject a delayed request after switching accounts or servers.</summary>
    public Func<bool>? IsCurrent { get; init; }

    public void ThrowIfNotCurrent()
    {
        if (IsCurrent?.Invoke() == false) throw new OperationCanceledException("Plex 操作所属的身份已改变");
    }

    public PlexCall Call => PlexCall.For(AccessToken, ClientIdentifier, ClientVersion);

    public override string ToString() => $"Plex 连接 {PlexEndpoint.ToDisplayString(ApiBase)}";
}

/// <summary>One query-string pair. Kept as a pair rather than a dictionary so a URL is reproducible.</summary>
public readonly record struct PlexParameter(string Key, string? Value);

/// <summary>
/// Paths and field names for the Plex Media Server HTTP API. The transport is the sole HTTP boundary;
/// this class owns what is asked and where, and <see cref="PlexMediaAdapter"/> owns how the answer maps
/// onto the shared browse model.
/// </summary>
public sealed partial class PlexClient(PlexHttp http)
{
    /// <summary>
    /// The library agent every scrobble and timeline report has to name. It is Plex's own constant for
    /// its bundled library metadata agent, and it is not a credential.
    /// </summary>
    public const string LibraryIdentifier = "com.plexapp.plugins.library";

    /// <summary>Unauthenticated server identity — the cheap way to ask 「这是不是一台 Plex」.</summary>
    public Task<JsonObject> GetIdentityAsync(Uri apiBase, PlexCall call, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(apiBase, "identity"), call, ct);

    /// <summary>
    /// The server's own description. Reached with the token, so it is also what proves a token is good
    /// for <em>this</em> server — an account token that the server has not been shared with answers 401.
    /// </summary>
    public Task<JsonObject> GetServerAsync(PlexConnection c, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, ""), c.Call, ct);

    public Task<JsonObject> GetSectionsAsync(PlexConnection c, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/sections"), c.Call, ct);

    public Task<JsonObject> GetSectionItemsAsync(PlexConnection c, string sectionKey, IReadOnlyList<PlexParameter> query,
        int start, int size, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/sections/" + PlexEndpoint.Segment(sectionKey) + "/all" + Query(query)),
            c.Call with { Headers = Page(start, size) }, ct);

    public Task<JsonObject> GetRecentlyAddedAsync(PlexConnection c, string? sectionKey, int start, int size,
        CancellationToken ct = default) => RootAsync(PlexEndpoint.Api(c.ApiBase, sectionKey is { Length: > 0 }
            ? "library/sections/" + PlexEndpoint.Segment(sectionKey) + "/recentlyAdded"
            : "library/recentlyAdded"), c.Call with { Headers = Page(start, size) }, ct);

    /// <summary>「继续观看」—— Plex calls it On Deck, and it is the server's own rule for what belongs there.</summary>
    public Task<JsonObject> GetOnDeckAsync(PlexConnection c, int start, int size, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/onDeck"), c.Call with { Headers = Page(start, size) }, ct);

    public Task<JsonObject> SearchAsync(PlexConnection c, string query, int start, int size, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "search" + Query([new("query", query)])),
            c.Call with { Headers = Page(start, size) }, ct);

    public Task<JsonObject> GetMetadataAsync(PlexConnection c, string ratingKey, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/metadata/" + PlexEndpoint.Segment(ratingKey)), c.Call, ct);

    /// <summary>Seasons of a show, or episodes of a season — Plex serves both from <c>children</c>.</summary>
    public Task<JsonObject> GetChildrenAsync(PlexConnection c, string ratingKey, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/metadata/" + PlexEndpoint.Segment(ratingKey) + "/children"), c.Call, ct);

    public Task<JsonObject> GetSimilarAsync(PlexConnection c, string ratingKey, int size, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/metadata/" + PlexEndpoint.Segment(ratingKey) + "/similar"),
            c.Call with { Headers = Page(0, size) }, ct);

    /// <summary>
    /// 已观看 / 未观看. Plex has no user-state endpoint of the Emby shape; these two <c>/:/</c> verbs are
    /// what its own clients call, and they answer with an empty container.
    /// </summary>
    public Task SetWatchedAsync(PlexConnection c, string ratingKey, bool watched, CancellationToken ct = default) =>
        http.GetAsync(PlexEndpoint.Special(c.ApiBase, watched ? ":/scrobble" : ":/unscrobble",
            Query([new("key", ratingKey), new("identifier", LibraryIdentifier)])), c.Call, ct);

    /// <summary>
    /// A playback report. Plex wants the position in milliseconds and the part key exactly as its own
    /// metadata spelled it; <paramref name="state"/> is <c>playing</c>, <c>paused</c> or <c>stopped</c>.
    /// </summary>
    public Task ReportTimelineAsync(PlexConnection c, string ratingKey, string partKey, string state,
        long positionTicks, long durationTicks, CancellationToken ct = default) =>
        http.GetAsync(PlexEndpoint.Special(c.ApiBase, ":/timeline", Query(
        [
            new("ratingKey", ratingKey),
            new("key", partKey),
            new("state", state),
            new("time", Milliseconds(positionTicks).ToString()),
            new("duration", Milliseconds(durationTicks).ToString()),
            new("identifier", LibraryIdentifier)
        ])), c.Call, ct);

    /// <summary>
    /// Exchanges a Plex account's credentials for an access token. This one request goes to Plex's
    /// account service rather than to the server; see <see cref="PlexHttp"/> for why.
    /// </summary>
    public Task<JsonObject> SignInToAccountAsync(string username, string password, PlexCall call,
        CancellationToken ct = default) => http.SignInToAccountAsync(username, password, call, ct);

    /// <summary>
    /// One image, by the path the item itself carried (<c>/library/metadata/…/thumb/…</c>). Plex scales
    /// on request, so the width is asked for rather than fetched and shrunk here.
    /// </summary>
    public Task<byte[]> GetImageAsync(PlexConnection c, string path, int? width, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new PlexApiException("该条目没有可用图片");
        var query = width is > 0 ? Query([new("width", Math.Clamp(width.Value, 1, 4096).ToString())]) : "";
        return http.GetBytesAsync(PlexEndpoint.Api(c.ApiBase, path.TrimStart('/') + query), c.Call, ct);
    }

    /// <summary>The original file. The token goes in the request headers, never in this address.</summary>
    public Uri GetMediaUrl(PlexConnection c, string partKey) =>
        PlexEndpoint.Api(c.ApiBase, partKey.TrimStart('/'));

    public IReadOnlyList<KeyValuePair<string, string>> GetMediaHeaders(PlexConnection c)
    {
        if (string.IsNullOrEmpty(c.AccessToken) || c.AccessToken.Any(char.IsControl))
            throw new PlexApiException("Plex 登录凭据无效");
        return [new("X-Plex-Token", c.AccessToken)];
    }

    private async Task<JsonObject> RootAsync(Uri uri, PlexCall call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = await http.GetJsonAsync(uri, call, ct).ConfigureAwait(false);
        return root["MediaContainer"] as JsonObject ?? root;
    }

    /// <summary>
    /// Plex pages with two headers rather than two query keys, which is why they are built here instead
    /// of being handed in with the rest of the query.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string>> Page(int start, int size) =>
    [
        new("X-Plex-Container-Start", Math.Max(0, start).ToString()),
        new("X-Plex-Container-Size", Math.Max(0, size).ToString())
    ];

    private static string Query(IReadOnlyList<PlexParameter> pairs)
    {
        var parts = pairs.Where(pair => pair.Value is { Length: > 0 })
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!));
        var text = string.Join('&', parts);
        return text.Length == 0 ? "" : "?" + text;
    }

    private static long Milliseconds(long ticks) => Math.Max(0, ticks) / TimeSpan.TicksPerMillisecond;
}
