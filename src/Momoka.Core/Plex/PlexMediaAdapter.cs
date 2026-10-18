using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.Plex;

/// <summary>
/// Converts the Plex Media Server API into the existing browse model. Request paths stay in
/// <see cref="PlexClient"/> and the established account/operation lifetime stays in <c>EmbySession</c>;
/// this class only decides what a Plex answer means.
/// </summary>
public sealed partial class PlexMediaAdapter(PlexClient client, PlexConnection connection)
{
    /// <summary>
    /// Identifies the server anonymously. <c>/identity</c> is the one endpoint that answers without a
    /// token, which is what makes it usable for 「测试连接」 and for the automatic product probe.
    /// </summary>
    public static async Task<PublicSystemInfo> DetectAsync(PlexClient client, string address, PlexCall call, CancellationToken ct)
    {
        var apiBase = PlexEndpoint.Normalize(address);
        var identity = await client.GetIdentityAsync(apiBase, call, ct).ConfigureAwait(false);
        var machine = Text(identity, "machineIdentifier");
        if (machine.Length == 0) throw new PlexApiException("该地址未返回可识别的 Plex 服务器信息");
        return new PublicSystemInfo
        {
            ProductName = "Plex",
            DetectedServerKind = MediaServerKind.Plex,
            ApiBase = apiBase,
            Version = Blank(Text(identity, "version")),
            Id = machine,
            ServerName = Blank(Text(identity, "friendlyName"))
        };
    }

    /// <summary>
    /// Signs in to a Plex account and then checks that the account may reach this server.
    /// <para>
    /// Two steps rather than one, because a Plex server does not verify an account password — Plex's
    /// account service does. The token it hands back is an <em>account</em> token, and whether that
    /// account has been shared this server is a separate question the server itself answers with a 401.
    /// A login that stopped at the first step would look successful and then fail on every browse.
    /// </para>
    /// </summary>
    public static async Task<EmbyConnection> SignInAsync(PlexClient client, Uri apiBase, string username, string password,
        DeviceIdentity device, string? version, string? serverId, CancellationToken ct)
    {
        var call = PlexCall.For(null, device.DeviceId, device.Version);
        JsonObject account;
        try
        {
            account = await client.SignInToAccountAsync(username, password, call, ct).ConfigureAwait(false);
        }
        catch (PlexApiException error) when (error.IsAuthenticationFailure)
        {
            throw new EmbyAuthenticationException("Plex 账号的用户名或密码不正确");
        }

        var token = Text(account, "authToken");
        var user = account["user"] as JsonObject;
        var accountId = user is null ? "" : Text(user, "id");
        var accountName = user is null ? "" : Text(user, "username") is { Length: > 0 } name ? name : Text(user, "title");
        if (token.Length == 0 || token.Any(char.IsControl) || accountId.Length == 0)
            throw new EmbyApiException("Plex 账号服务返回的登录结果缺少访问令牌或账号标识");

        var native = new PlexConnection(apiBase, token, accountId, accountName, device.DeviceId, device.Version);
        JsonObject server;
        try
        {
            server = await client.GetServerAsync(native, ct).ConfigureAwait(false);
        }
        catch (PlexApiException error) when (error.IsTokenExpired)
        {
            throw new EmbyAuthenticationException("Plex 账号已登录，但该账号没有这台服务器的访问权限");
        }

        var serverName = Text(server, "friendlyName");
        return new EmbyConnection(apiBase, token, accountId, accountName,
            serverName.Length > 0 ? serverName : apiBase.Host, device)
        {
            ServerKind = MediaServerKind.Plex,
            ServerVersion = Text(server, "version") is { Length: > 0 } reported ? reported : version,
            ServerId = Text(server, "machineIdentifier") is { Length: > 0 } machine ? machine : serverId
        };
    }

    public static EmbyApiException Translate(PlexApiException error) => error.IsTokenExpired
        ? new EmbyTokenExpiredException()
        : new EmbyApiException(error.Message, error.StatusCode);

    /// <summary>
    /// Confirms the token is still good for this server and answers who it belongs to.
    /// <para>
    /// The identity comes off the connection rather than out of the response: a Plex server has no
    /// per-user endpoint of the Emby shape, and the account a token belongs to was settled at sign-in.
    /// The request itself is the point — a revoked token, or one for an account the server has stopped
    /// sharing with, answers 401 here and the session re-authenticates.
    /// </para>
    /// </summary>
    public async Task<EmbyUser> GetCurrentUserAsync(CancellationToken ct)
    {
        await client.GetServerAsync(connection, ct).ConfigureAwait(false);
        return new EmbyUser { Id = connection.AccountId, Name = connection.AccountName, HasPassword = true };
    }

    public async Task<EmbySystemInfo> GetSystemInfoAsync(string name, string? version, CancellationToken ct)
    {
        await GetCurrentUserAsync(ct).ConfigureAwait(false);
        // Plex has no administrator-only server-information endpoint this client may read, so what is
        // reported is always the restricted subset. Marking it so is what keeps the settings page from
        // offering administration it cannot back up — and Plex offers no native administration here
        // anyway (see SettingsViewModel.AdministrationCategories).
        return new EmbySystemInfo { ServerName = name, Version = version, IsRestricted = true };
    }

    public async Task<List<EmbyItem>> GetViewsAsync(CancellationToken ct)
    {
        var container = await client.GetSectionsAsync(connection, ct).ConfigureAwait(false);
        // Music and photo libraries are dropped rather than shown: this client has no music face at all
        // and no photo browser, so a library reached through one would be a wall of folders that answers
        // a click with nothing worth looking at. Hidden rather than half-built, same as the shell's own
        // rule for Emby's music libraries.
        return Items(container)
            .Where(section => Text(section, "type") is "movie" or "show")
            .Select(Library)
            .ToList();
    }

    public async Task<ItemsResult> GetItemsAsync(ItemQuery query, CancellationToken ct)
    {
        if (query.IsFavorite) throw new NotSupportedException("Plex 不支持收藏列表");
        if (query.Filters is { IsBlank: false } || !string.IsNullOrEmpty(query.PersonId)
            || !string.IsNullOrEmpty(query.NameStartsWith))
            throw new NotSupportedException("Plex 尚不支持此浏览条件");
        if (query.IsResumable) return await QueryResumeAsync(query.StartIndex, query.Limit, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var container = await client.SearchAsync(connection, query.SearchTerm.Trim(), query.StartIndex, query.Limit, ct)
                .ConfigureAwait(false);
            var found = Items(container).Select(Item).Where(item => query.IncludeItemTypes.Count == 0
                || query.IncludeItemTypes.Contains(item.Type, StringComparer.Ordinal)).ToList();
            return new ItemsResult { Items = found, TotalRecordCount = Integer(container, "totalSize") ?? found.Count };
        }
        if (string.IsNullOrWhiteSpace(query.ParentId)) throw new NotSupportedException("Plex 需要指定媒体库才能浏览");

        var page = await client
            .GetSectionItemsAsync(connection, query.ParentId, Query(query), query.StartIndex, query.Limit, ct)
            .ConfigureAwait(false);
        var values = Items(page).Select(Item).ToList();
        return new ItemsResult { Items = values, TotalRecordCount = Integer(page, "totalSize") ?? values.Count };
    }

    public async Task<List<EmbyItem>> GetLatestAsync(string? parentId, int limit, string? types, CancellationToken ct) =>
        Items(await client.GetRecentlyAddedAsync(connection, parentId, 0, limit, ct).ConfigureAwait(false))
            .Select(Item).ToList();

    /// <summary>
    /// 随机挑选. Plex has no whole-server random: <c>sort=random</c> is a property of one library, so this
    /// samples every video library and shuffles the union. Bounded by the number of libraries, which on a
    /// real server is a handful.
    /// </summary>
    public async Task<List<EmbyItem>> GetRandomAsync(int limit, IReadOnlyList<string> types, CancellationToken ct)
    {
        if (limit <= 0) return [];
        var pool = new List<EmbyItem>();
        foreach (var library in await GetViewsAsync(ct).ConfigureAwait(false))
        {
            var page = await GetItemsAsync(new ItemQuery
            {
                ParentId = library.Id,
                Limit = Math.Max(limit, 20),
                IncludeItemTypes = types,
                SortBy = EmbySortBy.Random
            }, ct).ConfigureAwait(false);
            pool.AddRange(page.Items);
        }
        var items = pool.ToArray();
        Random.Shared.Shuffle(items);
        return [.. items.Take(limit)];
    }

    /// <summary>「继续观看」—— Plex's On Deck, which is the server's own rule for what belongs on that row.</summary>
    public async Task<ItemsResult> QueryResumeAsync(int startIndex, int limit, CancellationToken ct)
    {
        var container = await client.GetOnDeckAsync(connection, startIndex, limit, ct).ConfigureAwait(false);
        var values = Items(container).Select(Item).ToList();
        return new ItemsResult { Items = values, TotalRecordCount = Integer(container, "totalSize") ?? values.Count };
    }

    public async Task<EmbyItem> GetItemAsync(string id, CancellationToken ct)
    {
        var container = await client.GetMetadataAsync(connection, id, ct).ConfigureAwait(false);
        var item = Item(First(container));
        if (item.IsPlayable)
        {
            item.MediaSources = Sources(First(container), item.Id, client, connection);
            foreach (var source in item.MediaSources) source.RunTimeTicks ??= item.RunTimeTicks;
        }
        return item;
    }

    public async Task<List<EmbyItem>> GetSeasonsAsync(string seriesId, CancellationToken ct) =>
        Children(await client.GetChildrenAsync(connection, seriesId, ct).ConfigureAwait(false), EmbyItemType.Season, seriesId, seriesId)
            .ToList();

    public async Task<List<EmbyItem>> GetEpisodesAsync(string seriesId, string? seasonId, CancellationToken ct)
    {
        var seasons = string.IsNullOrEmpty(seasonId)
            ? (await GetSeasonsAsync(seriesId, ct).ConfigureAwait(false)).Select(item => item.Id).ToArray() : [seasonId];
        var episodes = new List<EmbyItem>();
        foreach (var season in seasons)
            episodes.AddRange(Children(await client.GetChildrenAsync(connection, season, ct).ConfigureAwait(false),
                EmbyItemType.Episode, season, seriesId));
        return episodes;
    }

    /// <summary>
    /// 接下来播放, scoped to one show. Plex answers it with On Deck, which is already ordered by its own
    /// idea of where the viewer is; this only keeps the rows belonging to <paramref name="seriesId"/>.
    /// </summary>
    public async Task<List<EmbyItem>> GetNextUpAsync(string? seriesId, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seriesId) || limit <= 0) return [];
        var container = await client.GetOnDeckAsync(connection, 0, Math.Max(limit, 20), ct).ConfigureAwait(false);
        return Items(container).Select(Item)
            .Where(item => string.Equals(item.SeriesId, seriesId, StringComparison.Ordinal))
            .Take(limit)
            .ToList();
    }

    public async Task<List<EmbyItem>> GetSimilarAsync(string itemId, int limit, CancellationToken ct)
    {
        if (limit <= 0) return [];
        var container = await client.GetSimilarAsync(connection, itemId, limit, ct).ConfigureAwait(false);
        return Items(container).Select(Item).ToList();
    }

    public Task<byte[]> GetImageAsync(string? path, int width, CancellationToken ct) =>
        string.IsNullOrEmpty(path)
            ? throw new NotSupportedException("该条目没有可用图片")
            : client.GetImageAsync(connection, path, width, ct);

    public async Task<EmbyUserData?> SetWatchedAsync(string id, bool watched, CancellationToken ct)
    {
        await client.SetWatchedAsync(connection, id, watched, ct).ConfigureAwait(false);
        return Item(First(await client.GetMetadataAsync(connection, id, ct).ConfigureAwait(false))).UserData;
    }

    public Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken ct) =>
        ReportAsync(report, "playing", ct);

    public Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken ct) =>
        ReportAsync(report, report.IsPaused ? "paused" : "playing", ct);

    public Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken ct) =>
        ReportAsync(report, "stopped", ct);

    private Task ReportAsync(PlaybackReport report, string state, CancellationToken ct) =>
        client.ReportTimelineAsync(connection, report.ItemId, report.PlexSelection?.PartKey ?? "", state,
            report.PositionTicks, report.PlexSelection?.DurationTicks ?? 0, ct);

    private static IEnumerable<EmbyItem> Children(JsonObject container, string type, string parentId, string seriesId) =>
        Items(container).Select(value =>
        {
            var item = Item(value);
            item.Type = type;
            item.IsFolder = type == EmbyItemType.Season;
            item.ParentId = parentId;
            item.SeriesId = seriesId;
            if (type == EmbyItemType.Episode) item.SeasonId = parentId;
            return item;
        });

    private static JsonObject First(JsonObject container) => Items(container).FirstOrDefault()
        ?? throw new PlexApiException("Plex 没有返回这个条目的信息");

    internal static string Text(JsonObject value, string key) => value[key]?.ToString() ?? "";
    internal static double? Number(JsonObject value, string key) => double.TryParse(Text(value, key),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number) ? number : null;
    internal static int? Integer(JsonObject value, string key) => Number(value, key) is >= int.MinValue and <= int.MaxValue and var number ? (int)number : null;
    internal static bool Flag(JsonObject value, string key) => Text(value, key) is "1" or "true";
    internal static long Ticks(double milliseconds) => (long)Math.Clamp(milliseconds * TimeSpan.TicksPerMillisecond, 0, long.MaxValue);
}
