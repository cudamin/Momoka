using System.Text.Json.Nodes;

namespace Momoka.FnNas;

public sealed record FnNasConnection(Uri ApiBase, string AccessToken)
{
    /// <summary>Management scopes reject a delayed request after switching accounts or servers.</summary>
    public Func<bool>? IsCurrent { get; init; }
    public void ThrowIfNotCurrent()
    {
        if (IsCurrent?.Invoke() == false) throw new OperationCanceledException("飞牛影视操作所属的身份已改变");
    }
    public override string ToString() => $"飞牛影视连接 {FnNasEndpoint.ToDisplayString(ApiBase)}";
}

/// <summary>
/// Paths and field names verified against the official 0.9.8 frontend / mediasrv 0.8.43.
/// JsonObject preserves version-specific fields; the transport is the sole HTTP boundary.
/// </summary>
public sealed partial class FnNasClient(FnNasHttp http)
{
    public Task<JsonObject> GetVersionAsync(Uri apiBase, CancellationToken ct = default) =>
        ObjectAsync(new(apiBase, ""), HttpMethod.Get, "api/v1/sys/version", null, ct);

    public Task<JsonObject> GetConfigurationAsync(Uri apiBase, CancellationToken ct = default) =>
        ObjectAsync(new(apiBase, ""), HttpMethod.Get, "api/v1/sys/config", null, ct);

    public async Task<string> LoginAsync(Uri apiBase, string username, string password, CancellationToken ct = default)
    {
        var data = await ObjectAsync(new(apiBase, ""), HttpMethod.Post, "api/v2/user/loginByPassword",
            new JsonObject { ["username"] = username.Trim(), ["password"] = FnNasSigner.HashPassword(password), ["app_name"] = "trimemedia-web" }, ct)
            .ConfigureAwait(false);
        var token = data["token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token) || token.Any(char.IsControl))
            throw new FnNasApiException("飞牛影视登录响应缺少有效凭据");
        return token;
    }

    public Task<JsonObject> GetUserAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/user/info", null, ct);

    /// <summary>
    /// 播放偏好页只改首选字幕语言与首选音频语言；正文就这两格，空串表示「任意语言」。
    /// 官方前端在每次下拉变更后立即提交，这里由页面合并成一次保存。
    /// </summary>
    public Task UpdateUserInfoAsync(FnNasConnection c, JsonObject draft, CancellationToken ct = default) =>
        ActionAsync(c, HttpMethod.Post, "api/v1/user/info", draft.DeepClone(), ct);

    /// <summary>
    /// 修改当前影视账号的密码。官方 0.9.8 的帐号页走 v2 并传 SHA-256；成功时 data 是新令牌，
    /// 帐号页拿它替换内存里的令牌，所以这里把它交回调用者。
    /// </summary>
    public async Task<string?> ChangePasswordAsync(FnNasConnection c, string newPassword, CancellationToken ct = default)
    {
        var data = await SendAsync(c, HttpMethod.Post, "api/v2/user/passwd",
            new JsonObject { ["password"] = FnNasSigner.HashPassword(newPassword) }, ct).ConfigureAwait(false);
        if (data is not JsonValue value || !value.TryGetValue<string>(out var token) || token.Length == 0) return null;
        if (token.Any(char.IsControl)) throw new FnNasApiException("飞牛影视密码修改响应缺少有效凭据");
        return token;
    }

    public Task LogoutAsync(FnNasConnection c, CancellationToken ct = default) =>
        ActionAsync(c, HttpMethod.Post, "api/v1/user/logout", null, ct);

    public Task<IReadOnlyList<JsonObject>> GetLibrariesAsync(FnNasConnection c, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/mediadb/list", null, ct);

    public Task<JsonObject> GetLibrarySummaryAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/mediadb/sum", null, ct);

    /// <summary>筛选面板的可选标签；<paramref name="ancestorGuid"/> 为空时是跨库查询。</summary>
    public Task<JsonObject> GetFilterTagsAsync(FnNasConnection c, string? ancestorGuid, bool favorites, CancellationToken ct = default)
    {
        var query = "api/v1/tag/list";
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(ancestorGuid))
            parameters.Add("ancestor_guid=" + Uri.EscapeDataString(ancestorGuid));
        if (favorites) parameters.Add("is_favorite=1");
        if (parameters.Count > 0) query += "?" + string.Join('&', parameters);
        return ObjectAsync(c, HttpMethod.Get, query, null, ct);
    }

    public Task<IReadOnlyList<JsonObject>> GetGenreTagsAsync(FnNasConnection c, string language, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/tag/genres?lan=" + Uri.EscapeDataString(language), null, ct);

    public Task<IReadOnlyList<JsonObject>> GetCountryTagsAsync(FnNasConnection c, string language, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/tag/iso3166?lan=" + Uri.EscapeDataString(language), null, ct);

    public Task<IReadOnlyList<JsonObject>> GetContinueWatchingAsync(FnNasConnection c, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/play/list", null, ct);

    public Task<JsonObject> GetItemsAsync(FnNasConnection c, JsonObject query, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Post, "api/v1/item/list", query, ct);

    public Task<JsonObject> GetFavoritesAsync(FnNasConnection c, JsonObject query, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Post, "api/v1/favorite/list", query, ct);

    public Task<IReadOnlyList<JsonObject>> SearchAsync(FnNasConnection c, string query, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/search/list?q=" + Uri.EscapeDataString(query), null, ct);

    public Task<JsonObject> GetItemAsync(FnNasConnection c, string guid, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/item/" + FnNasEndpoint.Segment(guid), null, ct);

    public Task<IReadOnlyList<JsonObject>> GetSeasonsAsync(FnNasConnection c, string tvGuid, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/season/list/" + FnNasEndpoint.Segment(tvGuid), null, ct);

    public Task<IReadOnlyList<JsonObject>> GetEpisodesAsync(FnNasConnection c, string seasonGuid, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/episode/list/" + FnNasEndpoint.Segment(seasonGuid), null, ct);

    public Task<byte[]> GetImageAsync(FnNasConnection c, string imagePath, int? width = null, CancellationToken ct = default)
    {
        var segments = imagePath.TrimStart('/').Split('/');
        var path = string.Join('/', segments.Select(FnNasEndpoint.Segment));
        var query = width is > 0 ? "?w=" + Math.Clamp(width.Value, 1, 4096) : "";
        return http.GetBytesAsync(FnNasEndpoint.Api(c.ApiBase, "api/v1/sys/img/" + path + query), c.AccessToken, ct);
    }

    public Task<JsonObject> GetPlayInfoAsync(FnNasConnection c, string itemGuid, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Post, "api/v1/play/info", new JsonObject { ["item_guid"] = itemGuid }, ct);

    /// <summary>The frontend passes the item's GUID, not the selected media file GUID.</summary>
    public Task<JsonObject> GetStreamsAsync(FnNasConnection c, string itemGuid, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/stream/list/" + FnNasEndpoint.Segment(itemGuid), null, ct);

    public Task ReportPlaybackAsync(FnNasConnection c, JsonObject record, CancellationToken ct = default) =>
        ActionAsync(c, HttpMethod.Post, "api/v1/play/record", record, ct);

    public Task RemoveContinueWatchingAsync(FnNasConnection c, string itemGuid, CancellationToken ct = default) =>
        ActionAsync(c, HttpMethod.Delete, "api/v1/play/record", new JsonObject { ["item_guid"] = itemGuid }, ct);

    public Task SetFavoriteAsync(FnNasConnection c, string itemGuid, bool favorite, CancellationToken ct = default) =>
        ActionAsync(c, favorite ? HttpMethod.Put : HttpMethod.Delete, "api/v1/item/favorite", new JsonObject { ["item_guid"] = itemGuid }, ct);

    public Task SetWatchedAsync(FnNasConnection c, string itemGuid, bool watched, CancellationToken ct = default) =>
        ActionAsync(c, watched ? HttpMethod.Post : HttpMethod.Delete, "api/v1/item/watched", new JsonObject { ["item_guid"] = itemGuid }, ct);

    public Uri GetMediaUrl(FnNasConnection c, string mediaGuid) =>
        FnNasEndpoint.Api(c.ApiBase, "api/v1/media/range/" + FnNasEndpoint.Segment(mediaGuid));

    public IReadOnlyList<KeyValuePair<string, string>> GetMediaHeaders(FnNasConnection c)
    {
        if (string.IsNullOrEmpty(c.AccessToken) || c.AccessToken.Any(char.IsControl))
            throw new FnNasApiException("飞牛影视登录凭据无效");
        return [new("Authorization", c.AccessToken)];
    }

    private Task<JsonNode?> SendAsync(FnNasConnection c, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        c.ThrowIfNotCurrent();
        ct.ThrowIfCancellationRequested();
        return http.SendAsync(method, FnNasEndpoint.Api(c.ApiBase, path), body, c.AccessToken, ct);
    }

    private async Task<JsonObject> ObjectAsync(FnNasConnection c, HttpMethod method, string path, JsonNode? body, CancellationToken ct) =>
        await SendAsync(c, method, path, body, ct).ConfigureAwait(false) as JsonObject ??
        throw new FnNasApiException("飞牛影视响应中的数据格式不正确");

    private async Task<IReadOnlyList<JsonObject>> ListAsync(FnNasConnection c, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var data = await SendAsync(c, method, path, body, ct).ConfigureAwait(false);
        if (data is null) return [];
        if (data is not JsonArray array || array.Any(item => item is not JsonObject))
            throw new FnNasApiException("飞牛影视响应中的列表格式不正确");
        return array.Cast<JsonObject>().ToArray();
    }

    private async Task ActionAsync(FnNasConnection c, HttpMethod method, string path, JsonNode? body, CancellationToken ct) =>
        _ = await SendAsync(c, method, path, body, ct).ConfigureAwait(false);
}
