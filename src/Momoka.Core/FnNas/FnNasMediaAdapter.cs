using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Diagnostics;

namespace Momoka.FnNas;

/// <summary>
/// Converts the official media API into the existing browse model. This boundary deliberately keeps
/// request paths in FnNasClient and the established account/operation lifetime in EmbySession.
/// </summary>
public sealed partial class FnNasMediaAdapter(FnNasClient client, FnNasConnection connection)
{
    public static async Task<PublicSystemInfo> DetectAsync(FnNasClient client, string address, CancellationToken ct)
    {
        var apiBase = FnNasEndpoint.Normalize(address);
        var version = await client.GetVersionAsync(apiBase, ct).ConfigureAwait(false);
        if (!Version.TryParse(Text(version, "version"), out _) || !Version.TryParse(Text(version, "mediasrvVersion"), out _))
            throw new FnNasApiException("该地址未返回可识别的飞牛影视版本");
        var configuration = await client.GetConfigurationAsync(apiBase, ct).ConfigureAwait(false);
        if (Text(configuration, "server_guid").Length == 0 || Text(configuration, "server_name").Length == 0)
            throw new FnNasApiException("该地址未返回可识别的飞牛影视服务器信息");
        return new PublicSystemInfo
        {
            ProductName = "飞牛影视",
            DetectedServerKind = MediaServerKind.FnNas,
            ApiBase = apiBase,
            Version = Text(version, "version"),
            Id = Text(configuration, "server_guid"),
            ServerName = Text(configuration, "server_name")
        };
    }

    public static async Task<EmbyConnection> SignInAsync(FnNasClient client, Uri apiBase, string username, string password,
        DeviceIdentity device, string? version, string? serverId, CancellationToken ct)
    {
        try
        {
            var token = await client.LoginAsync(apiBase, username, password, ct).ConfigureAwait(false);
            var native = new FnNasConnection(apiBase, token);
            var user = User(await client.GetUserAsync(native, ct).ConfigureAwait(false));
            var configuration = await OptionalReadAsync(() => client.GetConfigurationAsync(apiBase, ct), "sys/config", ct)
                .ConfigureAwait(false) ?? new JsonObject();
            var observedId = Text(configuration, "server_guid");
            if (!string.IsNullOrEmpty(serverId) && observedId.Length > 0 && observedId != serverId)
                throw new FnNasApiException("飞牛影视服务器身份已改变，请重新确认连接地址");
            var serverName = Text(configuration, "server_name");
            return new EmbyConnection(apiBase, token, user.Id, user.Name, serverName.Length > 0 ? serverName : apiBase.Host, device)
            {
                ServerKind = MediaServerKind.FnNas,
                ServerVersion = version,
                ServerId = observedId.Length > 0 ? observedId : serverId,
                IsAdministrator = user.Policy?.IsAdministrator == true
            };
        }
        catch (FnNasApiException error)
        {
            if (error.IsAuthenticationFailure) throw new EmbyAuthenticationException("飞牛影视拒绝了登录，请核对用户名、密码与访问权限");
            throw Translate(error);
        }
    }

    /// <summary>只让非关键补充信息缺失；调用方取消、身份过期、签名和无法解析的响应仍向上交给会话／页面。</summary>
    private static async Task<T?> OptionalReadAsync<T>(Func<Task<T>> read, string endpoint, CancellationToken ct) where T : class
    {
        try
        {
            var value = await read().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return value;
        }
        catch (FnNasApiException error) when (!ct.IsCancellationRequested && error.CanSkipOptionalRead)
        {
            Log.Warn("fnnas", $"{endpoint} 补充信息暂不可用，保留已取得的内容", error);
            return null;
        }
    }

    public static EmbyApiException Translate(FnNasApiException error) => error.IsTokenExpired
        ? new EmbyTokenExpiredException()
        : new EmbyApiException(error.Message, error.StatusCode);

    public async Task<EmbyUser> GetCurrentUserAsync(CancellationToken ct) =>
        User(await client.GetUserAsync(connection, ct).ConfigureAwait(false));

    public async Task<EmbySystemInfo> GetSystemInfoAsync(string name, string? version, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct).ConfigureAwait(false);
        return new EmbySystemInfo { ServerName = name, Version = version, IsRestricted = user.Policy?.IsAdministrator != true };
    }

    public static EmbyUser User(JsonObject value)
    {
        var id = Text(value, "guid");
        var name = Text(value, "username");
        if (id.Length == 0 || name.Length == 0) throw new FnNasApiException("飞牛影视用户信息缺少身份字段");
        return new EmbyUser { Id = id, Name = name, HasPassword = true, Policy = new() { IsAdministrator = Flag(value, "is_admin") } };
    }

    public async Task<List<EmbyItem>> GetViewsAsync(CancellationToken ct) =>
        (await client.GetLibrariesAsync(connection, ct).ConfigureAwait(false))
            .Where(value => Text(value, "category") != "IPTV").Select(Library).ToList();

    public async Task<ItemsResult> GetItemsAsync(ItemQuery query, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var matches = await client.SearchAsync(connection, query.SearchTerm, ct).ConfigureAwait(false);
            var items = matches.Select(Item).Where(item => query.IncludeItemTypes.Count == 0
                || query.IncludeItemTypes.Contains(item.Type, StringComparer.Ordinal));
            return Page(items, query.StartIndex, query.Limit);
        }
        if (query.IsResumable) return await QueryResumeAsync(query.StartIndex, query.Limit, ct).ConfigureAwait(false);
        var request = Query(query);
        var response = query.IsFavorite
            ? await client.GetFavoritesAsync(connection, request, ct).ConfigureAwait(false)
            : await client.GetItemsAsync(connection, request, ct).ConfigureAwait(false);
        var values = Objects(response, "list").Select(Item).ToList();
        var total = Integer(response, "total") ?? values.Count;
        var offset = Math.Max(0, query.StartIndex) % Math.Max(1, query.Limit);
        if (offset > 0 && values.Count == Math.Max(1, query.Limit) && query.StartIndex + values.Count < total + offset)
        {
            request["page"] = (Integer(request, "page") ?? 1) + 1;
            var next = query.IsFavorite
                ? await client.GetFavoritesAsync(connection, request, ct).ConfigureAwait(false)
                : await client.GetItemsAsync(connection, request, ct).ConfigureAwait(false);
            values.AddRange(Objects(next, "list").Select(Item));
        }
        return new ItemsResult { Items = values.Skip(offset).Take(Math.Max(0, query.Limit)).ToList(), TotalRecordCount = total };
    }

    public async Task<List<EmbyItem>> GetLatestAsync(string? parentId, int limit, string? types, CancellationToken ct) =>
        (await GetItemsAsync(new ItemQuery
        {
            ParentId = parentId,
            Limit = limit,
            SortBy = EmbySortBy.DateAdded,
            Descending = true,
            IncludeItemTypes = types?.Split(',') ?? []
        }, ct).ConfigureAwait(false)).Items;

    public async Task<List<EmbyItem>> GetRandomAsync(int limit, IReadOnlyList<string> types, CancellationToken ct)
    {
        if (limit <= 0) return [];
        var page = await GetItemsAsync(new ItemQuery { Limit = limit, IncludeItemTypes = types, SortBy = EmbySortBy.DateAdded }, ct).ConfigureAwait(false);
        if (page.TotalRecordCount > limit)
            page = await GetItemsAsync(new ItemQuery
            {
                StartIndex = Random.Shared.Next(page.TotalRecordCount - limit + 1),
                Limit = limit,
                IncludeItemTypes = types,
                SortBy = EmbySortBy.DateAdded
            }, ct).ConfigureAwait(false);
        var items = page.Items.ToArray();
        Random.Shared.Shuffle(items);
        return [.. items];
    }

    /// <summary>
    /// 轮播底图。飞牛的 <c>item/list</c> 只给一张海报（2:3），宽图（<c>backdrops</c>）只在条目详情里 ——
    /// 官方前端和 MoviePilot 都靠详情拿它（MoviePilot 拿不到 backdrops 时退到海报）。这里照做：按清单次序
    /// 补，最多问 <paramref name="slots"/> × 2 条，四条一批并发问。
    /// <para>
    /// 只给轮播用。货架上的卡画的是海报，不必为每一排多问一圈详情。列表里已经带宽图的条目（集的 16:9
    /// 剧照，见 <see cref="Item"/>) 不再问。
    /// </para>
    /// </summary>
    public async Task<List<EmbyItem>> WithCarouselArtworkAsync(IReadOnlyList<EmbyItem> items, int slots, CancellationToken ct)
    {
        var result = items.ToList();
        var budget = Math.Min(result.Count, Math.Max(0, slots) * 2);
        if (budget == 0) return result;

        var missing = new List<int>(budget);
        for (var index = 0; index < result.Count && missing.Count < budget; index++)
            if (ItemArtwork.Banner(result[index]) is null) missing.Add(index);

        for (var start = 0; start < missing.Count; start += CarouselBatch)
        {
            // 单集自带 16:9 剧照，凑够了就不再问 —— 一轮主页不该为一整排已经站得住的幻灯片多花十几次往返。
            if (HomeCarousel.Slides(result, slots).Count >= slots) break;
            var batch = missing.GetRange(start, Math.Min(CarouselBatch, missing.Count - start));
            var details = await Task.WhenAll(batch.Select(at => DetailArtworkAsync(result[at].Id, ct))).ConfigureAwait(false);
            for (var offset = 0; offset < batch.Count; offset++)
                if (details[offset] is { } detail) MergeArtwork(result[batch[offset]], detail);
        }

        return result;
    }

    /// <summary>一轮并发问几条详情。轮播等这一趟，条数多了会拖慢整个主页。</summary>
    private const int CarouselBatch = 4;

    private async Task<EmbyItem?> DetailArtworkAsync(string id, CancellationToken ct)
    {
        try
        {
            return Item(await client.GetItemAsync(connection, id, ct).ConfigureAwait(false));
        }
        catch (FnNasApiException error) when (!ct.IsCancellationRequested)
        {
            Log.Warn("fnnas", $"轮播底图读取失败：{id}", error);
            return null;
        }
    }

    /// <summary>把详情里的宽图补到列表条目上：背景图整列带走，艺术图与缩略图只在列表缺这一格时补。</summary>
    private static void MergeArtwork(EmbyItem item, EmbyItem detail)
    {
        if (item.BackdropImageTags.Count == 0) item.BackdropImageTags.AddRange(detail.BackdropImageTags);
        foreach (var kind in ArtworkKinds)
            if (!item.ImageTags.ContainsKey(kind) && detail.ImageTags.TryGetValue(kind, out var tag) && tag.Length > 0)
                item.ImageTags[kind] = tag;
    }

    private static readonly string[] ArtworkKinds = [EmbyImageStore.Art, EmbyImageStore.Thumb];

    public async Task<ItemsResult> QueryResumeAsync(int startIndex, int limit, CancellationToken ct) =>
        Page((await client.GetContinueWatchingAsync(connection, ct).ConfigureAwait(false)).Select(Item), startIndex, limit);

    public async Task<EmbyItem> GetItemAsync(string id, CancellationToken ct)
    {
        var item = Item(await client.GetItemAsync(connection, id, ct).ConfigureAwait(false));
        if (item.IsPlayable)
        {
            var streams = await OptionalReadAsync(() => client.GetStreamsAsync(connection, id, ct), "stream/list", ct).ConfigureAwait(false);
            if (streams is not null) item.MediaSources = Sources(streams, item.Id, client, connection);
            var progress = await OptionalReadAsync(() => client.GetPlayInfoAsync(connection, id, ct), "play/info", ct).ConfigureAwait(false);
            item.UserData ??= new();
            if (progress is not null)
            {
                if (Number(progress, "ts") is { } position) item.UserData.PlaybackPositionTicks = Ticks(position);
                if (Number(progress, "duration") is > 0 and var duration) item.RunTimeTicks = Ticks(duration);
            }
            if (item.RunTimeTicks is null or <= 0 && item.DefaultMediaSource?.RunTimeTicks is > 0 and var sourceDuration)
                item.RunTimeTicks = sourceDuration;
            foreach (var source in item.MediaSources)
                if (source.RunTimeTicks is null or <= 0 && item.RunTimeTicks is > 0)
                    source.RunTimeTicks = item.RunTimeTicks;
        }
        return item;
    }

    public async Task<List<EmbyItem>> GetSeasonsAsync(string seriesId, CancellationToken ct) =>
        (await client.GetSeasonsAsync(connection, seriesId, ct).ConfigureAwait(false)).Select(value =>
        {
            var item = Item(value); item.Type = EmbyItemType.Season; item.IsFolder = true;
            item.SeriesId = seriesId; item.ParentId = seriesId; return item;
        }).ToList();

    public async Task<List<EmbyItem>> GetEpisodesAsync(string seriesId, string? seasonId, CancellationToken ct)
    {
        var seasons = string.IsNullOrEmpty(seasonId)
            ? (await GetSeasonsAsync(seriesId, ct).ConfigureAwait(false)).Select(item => item.Id).ToArray() : [seasonId];
        var episodes = new List<EmbyItem>();
        foreach (var season in seasons)
            foreach (var value in await client.GetEpisodesAsync(connection, season, ct).ConfigureAwait(false))
            {
                var item = Item(value); item.Type = EmbyItemType.Episode; item.IsFolder = false;
                item.SeasonId = season; item.ParentId = season; item.SeriesId = seriesId; episodes.Add(item);
            }
        return episodes;
    }

    public async Task<List<EmbyItem>> GetNextUpAsync(string? seriesId, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seriesId) || limit <= 0) return [];
        var episodes = (await GetEpisodesAsync(seriesId, null, ct).ConfigureAwait(false))
            .OrderBy(item => item.ParentIndexNumber).ThenBy(item => item.IndexNumber).ToList();
        var lastWatched = episodes.FindLastIndex(item => item.UserData?.Played == true);
        return episodes.Skip(lastWatched + 1).Where(item => item.UserData?.Played != true).Take(limit).ToList();
    }

    public Task<byte[]> GetImageAsync(string? path, int width, CancellationToken ct) => string.IsNullOrEmpty(path)
        ? throw new NotSupportedException("该条目没有可用图片") : client.GetImageAsync(connection, path, width, ct);

    public async Task<EmbyUserData?> SetWatchedAsync(string id, bool watched, CancellationToken ct)
    {
        await client.SetWatchedAsync(connection, id, watched, ct).ConfigureAwait(false);
        return Item(await client.GetItemAsync(connection, id, ct).ConfigureAwait(false)).UserData;
    }

    public async Task<EmbyUserData?> SetFavoriteAsync(string id, bool favorite, CancellationToken ct)
    {
        await client.SetFavoriteAsync(connection, id, favorite, ct).ConfigureAwait(false);
        return Item(await client.GetItemAsync(connection, id, ct).ConfigureAwait(false)).UserData;
    }

    public Task ReportPlaybackAsync(PlaybackReport report, CancellationToken ct) =>
        client.ReportPlaybackAsync(connection, PlaybackRecord(report), ct);

    private static ItemsResult Page(IEnumerable<EmbyItem> source, int start, int limit)
    {
        var all = source.ToList();
        return new ItemsResult { TotalRecordCount = all.Count, Items = all.Skip(Math.Max(0, start)).Take(Math.Max(0, limit)).ToList() };
    }

    internal static string Text(JsonObject value, string key) => value[key]?.ToString() ?? "";
    internal static double? Number(JsonObject value, string key) => double.TryParse(Text(value, key), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;
    internal static int? Integer(JsonObject value, string key) => Number(value, key) is >= int.MinValue and <= int.MaxValue and var number ? (int)number : null;
    internal static bool Flag(JsonObject value, string key) => Text(value, key) is "1" or "true";
    internal static long Ticks(double seconds) => (long)Math.Clamp(seconds * TimeSpan.TicksPerSecond, 0, long.MaxValue);
    internal static IEnumerable<JsonObject> Objects(JsonObject value, string key) => value[key] is JsonArray array ? array.OfType<JsonObject>() : [];
}
