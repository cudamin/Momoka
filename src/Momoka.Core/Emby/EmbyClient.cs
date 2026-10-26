using System.Text.Json.Nodes;
using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>
/// Authenticated, user-scoped access to one Emby server. Immutable: a new sign-in
/// produces a new client rather than mutating shared state, which is what made v1's
/// settings object double as session state.
/// </summary>
public sealed partial class EmbyClient(EmbyHttp http, EmbyConnection connection)
{
    private const string Category = "emby";

    public EmbyConnection Connection { get; } = connection;

    public Uri ApiBase => Connection.ApiBase;

    private EmbyHttp.RequestContext Context => new(Connection.Device, Connection.AccessToken, ServerKind: Connection.ServerKind);

    private Momoka.FnNas.FnNasMediaAdapter? _fnNasAdapter;
    private Momoka.FnNas.FnNasMediaAdapter FnNasMedia => _fnNasAdapter ??= new(FnNas, FnNasConnection);

    private Momoka.Plex.PlexMediaAdapter? _plexAdapter;
    private Momoka.Plex.PlexMediaAdapter PlexMedia => _plexAdapter ??= new(Plex, PlexConnection);

    /// <summary>飞牛管理页通过已捕获的 session scope 取得协议客户端，不另保存或复制当前账号。</summary>
    public Momoka.FnNas.FnNasClient FnNas => Connection.IsFnNas ? http.FnNasClient
        : throw new NotSupportedException("当前连接不是飞牛影视");

    public Momoka.FnNas.FnNasConnection FnNasConnection => Connection.IsFnNas
        ? new(Connection.ApiBase, Connection.AccessToken)
        : throw new NotSupportedException("当前连接不是飞牛影视");

    public Momoka.Plex.PlexClient Plex => Connection.IsPlex ? http.PlexClient
        : throw new NotSupportedException("当前连接不是 Plex");

    /// <summary>
    /// The Plex view of the same connection. The client identifier is carried across so the server can
    /// attribute a stream to this device instead of inventing a new player per launch.
    /// </summary>
    public Momoka.Plex.PlexConnection PlexConnection => Connection.IsPlex
        ? new(Connection.ApiBase, Connection.AccessToken, Connection.UserId, Connection.UserName,
            Connection.Device.DeviceId, Connection.Device.Version)
        : throw new NotSupportedException("当前连接不是 Plex");

    // ---- browsing -------------------------------------------------------------

    public async Task<List<EmbyItem>> GetViewsAsync(CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetViewsAsync(cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetViewsAsync(cancellationToken).ConfigureAwait(false);
        // The only browse call that used to ask for no fields, and it shows on the library cards: no
        // ChildCount for the 「N 项」 line under one, and no PrimaryImageAspectRatio, which is the only
        // thing that says whether a library's cover art is poster-shaped or a wide banner before the
        // grid has downloaded it.
        var url = Url($"Users/{Connection.UserId}/Views", ("Fields", EmbyFields.Browse));
        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        Log.Debug(Category, $"读取到 {result.Items.Count} 个媒体库");
        return result.Items;
    }

    public Task<ItemsResult> GetItemsAsync(ItemQuery query, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return FnNasMedia.GetItemsAsync(query, cancellationToken);
        if (Connection.IsPlex) return PlexMedia.GetItemsAsync(query, cancellationToken);
        var url = Url($"Users/{Connection.UserId}/Items", query.ToParameters());
        return http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken);
    }

    /// <summary>飞牛筛选面板的可选标签，与 <c>item/list</c> 的 <c>tags</c> 同一套名字。</summary>
    public Task<Momoka.FnNas.FnNasFilterOptions> GetFnNasFilterOptionsAsync(
        string? parentId, bool favorites, string language, CancellationToken cancellationToken) =>
        Connection.IsFnNas ? FnNasMedia.GetFilterOptionsAsync(parentId, favorites, language, cancellationToken)
        : throw new NotSupportedException("当前服务器不支持飞牛筛选标签");

    /// <summary>
    /// 需求 5：the choices for one of <see cref="EmbyFilterBy.Lists"/> — which genres, tags or years the
    /// items in <paramref name="scope"/> actually have.
    /// <para>
    /// <c>/Genres</c>, <c>/Tags</c> and <c>/Years</c> each take the whole of <c>/Items</c>'s query and
    /// answer with the distinct values inside it, which is why one method serves all three: the key is
    /// the endpoint name. Emby 4.9 has no <c>/Items/Filters</c> — the endpoint other clients use for this
    /// was removed, and asking for it gets a 404 rather than an empty list.
    /// </para>
    /// <para>
    /// The scope deliberately keeps only where to look, not what is currently filtered. A list narrowed
    /// by the selection made from it is a list that empties itself as it is used: tick one genre and
    /// every other genre disappears, leaving no way back except 清除.
    /// </para>
    /// </summary>
    public async Task<List<string>> GetFilterValuesAsync(string key, ItemQuery scope, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) throw new NotSupportedException("飞牛影视不支持此筛选列表");
        if (Connection.IsPlex) throw new NotSupportedException("Plex 不支持此筛选列表");
        if (!EmbyFilterBy.Lists.Any(list => list.Key == key))
            throw new ArgumentException("未知的筛选列表", nameof(key));
        var mediaList = EmbyFilterBy.MediaListKeys.Contains(key);
        if (Connection.IsJellyfin && mediaList) throw new NotSupportedException("Jellyfin 不支持此筛选列表");
        if (Connection.IsJellyfin)
        {
            if (key is not (EmbyFilterBy.Keys.Genres or EmbyFilterBy.Keys.Tags or EmbyFilterBy.Keys.Years
                or EmbyFilterBy.Keys.OfficialRatings))
                throw new ArgumentException("未知的筛选列表", nameof(key));
            var filters = await http.GetJsonAsync<JsonObject>(Url("Items/Filters",
                ("UserId", Connection.UserId), ("ParentId", scope.ParentId),
                ("IncludeItemTypes", string.Join(',', scope.IncludeItemTypes)), ("MediaTypes", scope.MediaTypes)),
                Context, cancellationToken).ConfigureAwait(false);
            var values = filters.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
            var names = values?.Select(value => value?.ToString() ?? "").Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal).ToList() ?? [];
            return key == EmbyFilterBy.Keys.Years ? [.. names.OrderDescending(StringComparer.Ordinal)] : [.. names.Order(StringComparer.Ordinal)];
        }
        var query = new ItemQuery
        {
            ParentId = scope.ParentId,
            SearchTerm = scope.SearchTerm,
            IncludeItemTypes = scope.IncludeItemTypes,
            Recursive = scope.Recursive,
            SortBy = EmbySortBy.Name,
            // Years newest first, names A to Z. A year list starting at 1927 is one nobody scrolls.
            Descending = key == EmbyFilterBy.Keys.Years,
            // No fields: this answer is names, and a genre has no poster or overview worth the bytes.
            Fields = "",
            // Enough that no real library reaches it, and small enough to stay one round trip.
            Limit = 1000
        };

        var parameters = new List<(string, string?)> { ("UserId", Connection.UserId) };
        parameters.AddRange(query.ToParameters());

        var endpoint = key;
        if (key is EmbyFilterBy.Keys.AudioLanguages or EmbyFilterBy.Keys.SubtitleLanguages)
        {
            endpoint = "StreamLanguages";
            parameters.Add(("StreamType", key == EmbyFilterBy.Keys.AudioLanguages ? "Audio" : "Subtitle"));
        }
        if (mediaList)
        {
            parameters.Add(("EnableImages", "false"));
            parameters.Add(("EnableUserData", "false"));
        }
        var result = await http
            .GetJsonAsync<ItemsResult>(Url(endpoint, [.. parameters]), Context, cancellationToken)
            .ConfigureAwait(false);

        Log.Debug(Category, $"{key} 可选值 {result.Items.Count} 个");

        return [.. result.Items
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// 取一个条目。<paramref name="order"/> 是媒体源的排法（设置页「媒体源排序」，2026-09-24）：只有点名
    /// 「新入库在前」才去问各版本的入库时间并重排（多一次旁路请求，见 <see cref="OrderVersionsByAddedAsync"/>）；
    /// 「默认」就是服务器给的次序，一个字节都不多问。详情页与播放器起播都从这一条拿条目，所以那两处界面
    /// 读到的版本表次序天然一致。
    /// </summary>
    public async Task<EmbyItem> GetItemAsync(string itemId, CancellationToken cancellationToken,
        string fields = EmbyFields.Detail, MediaSourceOrder order = MediaSourceOrder.Default)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        var url = Url($"Users/{Connection.UserId}/Items/{itemId}", ("Fields", fields));
        var item = await http.GetJsonAsync<EmbyItem>(url, Context, cancellationToken).ConfigureAwait(false);
        if (order == MediaSourceOrder.NewestFirst)
            await OrderVersionsByAddedAsync(item, cancellationToken).ConfigureAwait(false);
        return item;
    }

    /// <summary>
    /// 多版本条目的版本表按<b>入库时间</b>重排，最新入库的排最前（2026-09-24 用户令）。只在设置点名
    /// 「新入库在前」时被调（<see cref="GetItemAsync"/> 的 <c>order</c>）。拿不到时间就保持
    /// 服务器给的次序 —— 一次排序的旁路请求不该绊倒详情本身，失败只记一笔。
    /// <para>
    /// 每一版的入库时间服务器不在媒体源上给（MediaSourceInfo 一个日期字段都没有），但每一版在服务器上
    /// 本来就是一个条目（<see cref="MediaSource.ItemId"/>），它的 <c>DateCreated</c> 就是入库时间。实测
    /// （Emby 4.10，探针在 <c>work/versions-probe</c>）：备选版本条目被服务器藏起来，
    /// <c>/Users/{uid}/Items?Ids=…</c> 只回主条目；<b>不带用户前缀的 <c>/Items?Ids=…</c> 全都认</b>，
    /// 一条请求问齐全部版本。单版本条目根本不发这一条。
    /// </para>
    /// </summary>
    private async Task OrderVersionsByAddedAsync(EmbyItem item, CancellationToken cancellationToken)
    {
        var ids = item.MediaSources
            .Select(source => source.ItemId)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count < 2) return;

        try
        {
            var url = Url("Items",
                ("Ids", string.Join(',', ids)),
                ("Fields", "DateCreated"));
            var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
            var added = result.Items
                .Where(version => version.DateCreated is not null)
                .ToDictionary(version => version.Id, version => version.DateCreated!.Value);

            ItemDetail.OrderVersionsNewestFirst(item, added);
            Log.Debug(Category, $"《{item.Name}》{item.MediaSources.Count} 版已按入库时间排好（最新在前）");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Warn(Category, "版本的入库时间没拿到，保持服务器次序", error);
        }
    }

    /// <summary>
    /// 服务器自己那条 Resume 接口 —— <b>「继续观看」的全部口径都在它手里</b>：有播放进度、没标已看、
    /// 没被 <c>HideFromResume</c> 移出。主页那一排和那一排点进去的那一页都从这里拿，两处就不可能再对不上。
    /// <para>
    /// 2026-09-22 用户报「首页的继续观看怎么只有一项了」：查下来那一排本来就只有一项（服务器上「有播放进度」
    /// 的九条里，五条已标已看、三条被移出继续观看），真正错的是点进去那一页 —— 它走通用查询
    /// <c>Filters=IsResumable</c>，装了九条，于是「一排 1 项、进去 9 项」。
    /// </para>
    /// <para>
    /// 这个差别在通用查询里补不出来，所以那一页只能问接口本身：实测 <c>Filters=IsResumable,IsUnplayed</c>
    /// 只收到四条（还差被移出的那三条），而「被移出」这个条件通用查询里没有词可以表达 —— 把
    /// <c>NotHiddenFromResume</c> 塞进 Filters，服务器一个都不认、静默丢掉，照样回四条（未知筛选词不报错）。
    /// </para>
    /// <para>
    /// <c>StartIndex</c> 这条接口是真认的（实测 <c>StartIndex=1&amp;Limit=1</c> 会真的跳过头一条），所以点进去
    /// 那一页照旧能往下翻。<c>SortBy</c> 与 <c>Filters</c> 一概不发：这一页的次序和口径都由服务器定，
    /// 界面上那两颗键在这一页也就收起来了（见 <c>LibraryViewModel.SortFilterVisibility</c>）。
    /// </para>
    /// </summary>
    /// <param name="startIndex">翻页用的起点；<see cref="GetResumeAsync"/> 传 0。</param>
    /// <param name="parentId">
    /// 只看这一个库里的续播（电视剧库「推荐」页那一排）。这条接口在官方规格里认 <c>ParentId</c>；不传就是整台
    /// 服务器，主页那一排和「继续观看」那一页走的都是不传的这一档。飞牛与 Plex 的适配各自没有这个筛选，
    /// 传进来也被忽略 —— 那两家的推荐页本来就不存在。
    /// </param>
    public async Task<ItemsResult> QueryResumeAsync(int startIndex, int limit, CancellationToken cancellationToken,
        string? parentId = null)
    {
        if (Connection.IsFnNas) return await FnNasMedia.QueryResumeAsync(startIndex, limit, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.QueryResumeAsync(startIndex, limit, cancellationToken).ConfigureAwait(false);
        var url = Url($"Users/{Connection.UserId}/Items/Resume",
            ("StartIndex", startIndex.ToString()),
            ("Limit", limit.ToString()),
            ("Fields", EmbyFields.Browse),
            ("MediaTypes", "Video"),
            ("ParentId", parentId));
        return await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>主页那一排只要清单，不要总数 —— 接口本身见 <see cref="QueryResumeAsync"/>。</summary>
    public async Task<List<EmbyItem>> GetResumeAsync(int limit, CancellationToken cancellationToken) =>
        (await QueryResumeAsync(startIndex: 0, limit, cancellationToken).ConfigureAwait(false)).Items;

    /// <summary>Note: this endpoint returns a bare array rather than an ItemsResult.</summary>
    /// <param name="includeItemTypes">
    /// 逗号分隔的类型表（<see cref="HomeCarousel.LatestTypes"/> 的产物），null = 不加参数 —— 轮播「全部
    /// 媒体」那一档（用户的话，2026-09-13「轮播图要使用什么媒体」）。
    /// </param>
    public Task<List<EmbyItem>> GetLatestAsync(
        string? parentId,
        int limit,
        CancellationToken cancellationToken,
        string? includeItemTypes = null,
        bool unplayedOnly = true,
        bool groupItems = true)
    {
        if (Connection.IsFnNas) return FnNasMedia.GetLatestAsync(parentId, limit, includeItemTypes, cancellationToken);
        if (Connection.IsPlex) return PlexMedia.GetLatestAsync(parentId, limit, includeItemTypes, cancellationToken);
        var url = Url($"Users/{Connection.UserId}/Items/Latest",
            ("Limit", limit.ToString()),
            ("Fields", EmbyFields.Browse),
            ("ParentId", parentId),
            ("IsPlayed", unplayedOnly ? "false" : null),
            ("GroupItems", groupItems ? "true" : null),
            ("IncludeItemTypes", includeItemTypes));
        return http.GetJsonAsync<List<EmbyItem>>(url, Context, cancellationToken);
    }

    /// <summary>
    /// 随机的一批条目 —— 轮播「随机挑选」来源那一趟（「要使用最近添加还是随机的」，用户的话，2026-09-13）。
    /// <see cref="GetLatestAsync"/> 走的 /Items/Latest 没有随机这一档，走通用的 /Items、SortBy=Random ——
    /// 和浏览页排序菜单里「随机」同一个值（<see cref="EmbySortBy.Random"/>）。
    /// </summary>
    public async Task<List<EmbyItem>> GetRandomAsync(
        int limit,
        IReadOnlyList<string> includeItemTypes,
        CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetRandomAsync(limit, includeItemTypes, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetRandomAsync(limit, includeItemTypes, cancellationToken).ConfigureAwait(false);
        var url = Url($"Users/{Connection.UserId}/Items", new ItemQuery
        {
            Recursive = true,
            IncludeItemTypes = includeItemTypes,
            SortBy = EmbySortBy.Random,
            Limit = limit
        }.ToParameters());

        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    /// 轮播候选补齐宽图。飞牛的列表只发一张 2:3 海报，宽图在条目详情里（见
    /// <see cref="Momoka.FnNas.FnNasMediaAdapter.WithCarouselArtworkAsync"/>）；其余服务器的列表本来就带背景图，
    /// 原样交回。
    /// </summary>
    public Task<List<EmbyItem>> WithCarouselArtworkAsync(
        IReadOnlyList<EmbyItem> items,
        int slots,
        CancellationToken cancellationToken) =>
        Connection.IsFnNas
            ? FnNasMedia.WithCarouselArtworkAsync(items, slots, cancellationToken)
            : Task.FromResult(items.ToList());

    /// <summary>
    /// 接下来播放. <paramref name="seriesId"/> narrows it to one show, which is what a series page's
    /// 「尚未观看」 row is: the same endpoint the home page uses, scoped, so the two can never disagree
    /// about which episode is next.
    /// </summary>
    public async Task<List<EmbyItem>> GetNextUpAsync(int limit, CancellationToken cancellationToken,
        string? seriesId = null, string? parentId = null)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetNextUpAsync(seriesId, limit, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetNextUpAsync(seriesId, limit, cancellationToken).ConfigureAwait(false);
        var url = Url("Shows/NextUp",
            ("UserId", Connection.UserId),
            ("SeriesId", seriesId),
            ("ParentId", parentId),
            ("Limit", limit.ToString()),
            ("Fields", EmbyFields.Browse));
        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    /// Jellyfin's 「建议」 rows for one library: <c>GET /Movies/Recommendations</c> groups films by why
    /// they were suggested and names the film the reason is based on. Emby has no such endpoint; the
    /// library's movie suggestions page therefore uses only the resume/latest rows there.
    /// </summary>
    public async Task<List<MovieRecommendation>> GetMovieRecommendationsAsync(
        string parentId, int limit, CancellationToken cancellationToken)
    {
        if (!Connection.IsJellyfin) throw new NotSupportedException("当前服务器不支持电影建议");
        var url = Url("Movies/Recommendations",
            ("UserId", Connection.UserId),
            ("ParentId", parentId),
            ("Fields", EmbyFields.Browse),
            ("CategoryLimit", "6"),
            ("ItemLimit", limit.ToString()));
        return await http.GetJsonAsync<List<MovieRecommendation>>(url, Context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<EmbyItem>> GetSeasonsAsync(string seriesId, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetSeasonsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetSeasonsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        var url = Url($"Shows/{seriesId}/Seasons",
            ("UserId", Connection.UserId),
            ("Fields", EmbyFields.Browse));
        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    public async Task<List<EmbyItem>> GetEpisodesAsync(string seriesId, string? seasonId, CancellationToken cancellationToken, string fields = EmbyFields.Browse)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetEpisodesAsync(seriesId, seasonId, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetEpisodesAsync(seriesId, seasonId, cancellationToken).ConfigureAwait(false);
        var url = Url($"Shows/{seriesId}/Episodes",
            ("UserId", Connection.UserId),
            ("SeasonId", seasonId),
            ("SortBy", EmbySortBy.Number),
            ("SortOrder", "Ascending"),
            ("Fields", fields));
        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    /// 遗漏集 — 服务器按刮削源的元数据算出来的「这部剧本该有、而库里没有」的单集。
    /// <para>
    /// 走 Emby 自己那条 <c>Shows/Missing</c>（4.8 起就有，官方网页端那三点的菜单里也是它），而不是给通用
    /// 查询加一个 <c>IsMissing</c> 筛选：那一位是 Jellyfin 的，Emby 收到会静默丢掉（见
    /// <see cref="EmbyFilterBy"/> 那段），发出去看着像做成了、其实什么都没筛。
    /// </para>
    /// <para>
    /// <paramref name="seriesId"/> 走 <c>ParentId</c>：这条接口认的参数不多（<c>StartIndex</c>、
    /// <c>Limit</c>、<c>ParentId</c>、<c>IsUnaired</c>、<c>IsSpecialSeason</c>），而 <c>ParentId</c> 是它
    /// 承诺能把结果收窄到一部剧的那一个。季那一档由调用方在本地按季号筛（见
    /// <see cref="ItemDetail.MissingInSeason"/>）—— 拿季 id 当 <c>ParentId</c> 传进去不一定认，而季号是每条
    /// 遗漏集自己带着的一位，稳。
    /// </para>
    /// <para>
    /// <b>两个筛选都是三态，null 才是「不发这一条」。</b>规格里它们叫 <c>IsUnaired</c> 和
    /// <c>IsSpecialSeason</c> —— 是筛选（<c>true</c> 只要这一类、<c>false</c> 只要另一类），不是「要不要带
    /// 上」。这一点 2026-10-10 实测过：早先照社区帖子里那对 <c>IncludeUnaired</c>／<c>IncludeSpecials</c> 发，
    /// 服务器一个都不认、静默丢掉，四种组合问回来的条数一模一样。
    /// </para>
    /// </summary>
    /// <param name="isUnaired">
    /// true 只要还没播出的、false 只要已经播出的、null 两类都要。null 是这张表的常态：两颗开关都关着时，
    /// 「哪些算没播出」由 <see cref="ItemDetail.MissingIsUnaired"/> 在本地判，不必多问一趟。
    /// </param>
    /// <param name="isSpecialSeason">true 只要特别篇（第 0 季）、false 只要正片、null 两类都要。</param>
    public async Task<List<EmbyItem>> GetMissingEpisodesAsync(
        string seriesId,
        bool? isUnaired,
        bool? isSpecialSeason,
        CancellationToken cancellationToken)
    {
        var url = Url(Connection.IsJellyfin ? "Items" : "Shows/Missing",
            ("UserId", Connection.UserId),
            ("ParentId", seriesId),
            ("IncludeItemTypes", EmbyItemType.Episode),
            ("Recursive", "true"),
            ("SortBy", EmbySortBy.Number),
            ("SortOrder", "Ascending"),
            ("Fields", EmbyFields.Browse),
            ("Limit", "500"),
            ("IsUnaired", isUnaired is { } unaired ? (unaired ? "true" : "false") : null),
            ("IsMissing", Connection.IsJellyfin ? "true" : null),
            ("IsSpecialSeason", !Connection.IsJellyfin && isSpecialSeason is { } special ? (special ? "true" : "false") : null));

        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        if (Connection.IsJellyfin && isSpecialSeason is { } onlySpecial)
            result.Items.RemoveAll(item => (item.ParentIndexNumber == 0) != onlySpecial);
        Log.Debug(Category, $"读取到 {result.Items.Count} 集遗漏（未播出={isUnaired?.ToString() ?? "都要"}，"
            + $"特别篇={isSpecialSeason?.ToString() ?? "都要"}）");
        return result.Items;
    }

    /// <summary>
    /// 服务器上现有的合集，「添加到合集」那张表列的就是这些。
    /// <para>
    /// 不要字段：这张表只写名字。上限给到能装下任何一台真实服务器，而且仍旧是一趟请求。
    /// </para>
    /// </summary>
    public async Task<List<EmbyItem>> GetCollectionsAsync(CancellationToken cancellationToken)
    {
        var result = await GetItemsAsync(
            new ItemQuery
            {
                Recursive = true,
                IncludeItemTypes = [EmbyItemType.BoxSet],
                SortBy = EmbySortBy.Name,
                Fields = "",
                Limit = 500
            },
            cancellationToken).ConfigureAwait(false);

        Log.Debug(Category, $"读取到 {result.Items.Count} 个合集");
        return result.Items;
    }

    /// <summary>
    /// 更多类似 — the recommendations Emby's own detail page shows under the cast row. Asked with the
    /// same browse fields, so a recommendation card is a card like any other and needs no template of
    /// its own.
    /// </summary>
    public async Task<List<EmbyItem>> GetSimilarAsync(string itemId, int limit, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return [];
        if (Connection.IsPlex) return await PlexMedia.GetSimilarAsync(itemId, limit, cancellationToken).ConfigureAwait(false);
        var url = Url($"Items/{itemId}/Similar",
            ("UserId", Connection.UserId),
            ("Limit", limit.ToString()),
            ("Fields", EmbyFields.Browse));
        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    // ---- user state -----------------------------------------------------------

    /// <summary>
    /// 标记为已观看. The answer is the item's user data as the server now sees it, which is more than an
    /// echo: marking a film played also clears its resume position, and marking a series played empties
    /// the remaining-episode count on every card that shows one. Null when the server sent no body.
    /// </summary>
    public Task<EmbyUserData?> MarkPlayedAsync(string itemId, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return FnNasMedia.SetWatchedAsync(itemId, true, cancellationToken);
        if (Connection.IsPlex) return PlexMedia.SetWatchedAsync(itemId, true, cancellationToken);
        var url = Url($"Users/{Connection.UserId}/PlayedItems/{itemId}");
        return http.PostForJsonAsync<EmbyUserData>(url, null, Context, cancellationToken);
    }

    /// <inheritdoc cref="MarkPlayedAsync"/>
    public Task<EmbyUserData?> MarkUnplayedAsync(string itemId, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return FnNasMedia.SetWatchedAsync(itemId, false, cancellationToken);
        if (Connection.IsPlex) return PlexMedia.SetWatchedAsync(itemId, false, cancellationToken);
        var url = Url($"Users/{Connection.UserId}/PlayedItems/{itemId}");
        return http.DeleteForJsonAsync<EmbyUserData>(url, Context, cancellationToken);
    }

    /// <inheritdoc cref="MarkPlayedAsync"/>
    public Task<EmbyUserData?> SetFavoriteAsync(string itemId, bool isFavorite, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return FnNasMedia.SetFavoriteAsync(itemId, isFavorite, cancellationToken);
        // Plex's server API has no per-item favourite flag at all — favourites there belong to the
        // account service, not to the library — so this is refused rather than faked as a label.
        if (Connection.IsPlex) throw new NotSupportedException("Plex 不支持收藏条目");
        var url = Url($"Users/{Connection.UserId}/FavoriteItems/{itemId}");
        return isFavorite
            ? http.PostForJsonAsync<EmbyUserData>(url, null, Context, cancellationToken)
            : http.DeleteForJsonAsync<EmbyUserData>(url, Context, cancellationToken);
    }

    /// <summary>飞牛状态写入确认后的独立补读；只取条目用户状态，不重新提交已确认的操作。</summary>
    public Task<EmbyUserData?> GetFnNasUserDataAsync(string itemId, CancellationToken cancellationToken) =>
        Connection.IsFnNas ? FnNasMedia.GetUserDataAsync(itemId, cancellationToken)
            : throw new NotSupportedException("当前服务器不是飞牛影视");

    /// <summary>
    /// 从继续观看中移除。**进度留着** —— 服务器只是给这个条目按下一位「不要列进继续观看」，下次打开它照旧从
    /// 上次的地方接着播。想连进度一起清掉的是 <see cref="MarkUnplayedAsync"/>，那是另一条菜单。
    /// </summary>
    /// <inheritdoc cref="MarkPlayedAsync"/>
    public Task<EmbyUserData?> HideFromResumeAsync(string itemId, bool hide, CancellationToken cancellationToken)
    {
        var url = Url($"Users/{Connection.UserId}/Items/{itemId}/HideFromResume",
            ("Hide", hide ? "true" : "false"));
        return http.PostForJsonAsync<EmbyUserData>(url, null, Context, cancellationToken);
    }

    // ---- metadata editing -----------------------------------------------------

    /// <summary>
    /// The item exactly as the server sent it, unparsed. 编辑元数据 has to give the whole object back on
    /// save — Emby's update endpoint takes a complete item and treats every field it can edit as
    /// authoritative, so anything dropped on the way through is a field the user just erased. Keeping the
    /// JSON means only the fields the form actually touches can change, including the dozens
    /// <see cref="EmbyItem"/> has no property for.
    /// </summary>
    public Task<JsonObject> GetItemJsonAsync(string itemId, CancellationToken cancellationToken)
    {
        var url = Url($"Users/{Connection.UserId}/Items/{itemId}");
        return http.GetJsonAsync<JsonObject>(url, Context, cancellationToken);
    }

    /// <summary>
    /// Writes an edited item back. Administrators only — a normal account gets 403, which is a thing to
    /// report rather than to hide, since nothing about the item is wrong.
    /// </summary>
    public Task UpdateItemAsync(string itemId, JsonObject item, CancellationToken cancellationToken) =>
        http.PostAsync(Url($"Items/{itemId}"), item, Context, cancellationToken);

    // ---- 合集、刮削、扫库、删除 ------------------------------------------------

    /// <summary>新建一个合集，并把这几个条目放进去。答案里带着新合集的 id。</summary>
    public Task<CollectionCreationResult> CreateCollectionAsync(
        string name,
        IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken) =>
        http.PostJsonAsync<CollectionCreationResult>(
            Url("Collections",
                ("Name", name),
                ("Ids", string.Join(',', itemIds)),
                // 不锁：锁住的合集不再接受刮削，而这一步只是「把几样东西归到一处」。
                ("IsLocked", "false")),
            null,
            Context,
            cancellationToken);

    /// <summary>把这几个条目加进一个已有的合集。已经在里头的会被服务器忽略，不是错。</summary>
    public Task AddToCollectionAsync(
        string collectionId,
        IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken) =>
        http.PostAsync(
            Url($"Collections/{collectionId}/Items", ("Ids", string.Join(',', itemIds))),
            null,
            Context,
            cancellationToken);

    /// <summary>
    /// 刷新元数据，按「刷新元数据」那张表上选的那一档来（照 Emby 网页端 <c>components/refreshdialog</c> 的
    /// 做法）。<paramref name="mode"/> 两档：<c>missing</c> 是「问一遍刮削源，缺什么补什么、现有的一个字不动」，
    /// <c>all</c> 是「抓回来的盖掉现在的」。
    /// <para>
    /// <b>两档都先去问刮削源</b>（<c>MetadataRefreshMode=FullRefresh</c>）—— 网页端那两档发的是同一个
    /// <c>FullRefresh</c>，分岔只在 <c>ReplaceAllMetadata</c> 这一位上。它写反了的代价是「刷新一下，手改过的
    /// 简介没了」，而屏上什么都看不出来，所以这一位认的是档位自己那个字符串，不认调用方顺手传的一个 bool。
    /// </para>
    /// <para>
    /// <paramref name="replaceImages"/> / <paramref name="replaceThumbnails"/> 是那张表上另外两颗开关，默认都
    /// <b>关</b>：用户刚在「修改媒体封面图」里挑的那张封面，不该被一次刷新扔掉；而开了的那一颗会先删光再重下，
    /// 是有可能让可用图比以前更少的一件事，所以由用户在表上单独点。缺图的条目照旧会补上 —— 那是「补缺」不是
    /// 「覆盖」。
    /// </para>
    /// <para>
    /// 递归：一部剧刷新的时候，季和单集跟着一起来 —— 不然「刷新了却还是缺集名」。
    /// </para>
    /// </summary>
    public Task RefreshItemAsync(
        string itemId,
        string mode,
        bool replaceImages,
        bool replaceThumbnails,
        CancellationToken cancellationToken)
    {
        if (mode is not ("missing" or "all")) throw new ArgumentException("未知的刷新模式。");

        return http.PostAsync(
            Url($"Items/{itemId}/Refresh",
                ("Recursive", "true"),
                ("MetadataRefreshMode", "FullRefresh"),
                ("ImageRefreshMode", "FullRefresh"),
                ("ReplaceAllMetadata", mode == "all" ? "true" : "false"),
                ("ReplaceAllImages", replaceImages ? "true" : "false"),
                ("ReplaceThumbnailImages", replaceThumbnails ? "true" : "false")),
            null,
            Context,
            cancellationToken);
    }

    /// <summary>
    /// 自检专用：只走一遍 <c>Items/{id}/Refresh</c>，什么都不改（<c>MetadataRefreshMode=ValidationOnly</c>）。
    /// 用来确认这条路真的通着，而不用拿用户的真实库去试一次覆盖。
    /// <para>
    /// 单独一支而不是给 <see cref="RefreshItemAsync"/> 添一个开关：那一头收的是「刷新元数据」那张表上的两档，
    /// 而这一趟根本不是一次刷新 —— 混进去的下场是有一天有人把 <c>ValidationOnly</c> 当成第三档挂到界面上，
    /// 于是自检每一轮都真去刮一次用户的库。
    /// </para>
    /// </summary>
    public Task ValidateItemRefreshAsync(string itemId, CancellationToken cancellationToken) =>
        http.PostAsync(
            Url($"Items/{itemId}/Refresh",
                ("Recursive", "true"),
                ("MetadataRefreshMode", "ValidationOnly"),
                ("ImageRefreshMode", "ValidationOnly")),
            null,
            Context,
            cancellationToken);

    /// <summary>
    /// 重新扫描媒体库 —— 整台服务器扫一遍，也就是网页控制台里那颗「扫描媒体库」。管理员才能用。
    /// <para>
    /// 不按单个库扫：一张卡片上问不出它属于哪个库（单集的 <c>ParentId</c> 是季，不是库），而「在继续观看上扫
    /// 整台、在媒体库里只扫这一个」会让同一条菜单在两处做两件事。
    /// </para>
    /// </summary>
    public Task ScanLibraryAsync(CancellationToken cancellationToken) =>
        http.PostAsync(Url("Library/Refresh"), null, Context, cancellationToken);

    // ---- 识别 ----------------------------------------------------------------
    //
    // 四条路拼成一件事（Emby 网页端的「识别」，协议见 ItemIdentify 的类注释）：问这个条目能填哪几家的标识符、
    // 按表单去刮削源找候选、把挑中的一条落下来、把识别整个抹掉。

    /// <summary>
    /// 这个条目能填哪几家的标识符。服务器按条目类型答 —— 电影和剧集拿到的不是同一份。
    /// <para>
    /// <c>IsSupportedAsIdentifier=true</c> 是网页端加的（见 <c>components/itemidentifier</c>）：它把那些
    /// 「能看不能用」的 provider（比如只能读不能写的）滤掉，留下的每一家都是真能填进 <c>ProviderIds</c> 的。
    /// </para>
    /// </summary>
    public Task<List<ExternalIdInfo>> GetExternalIdInfosAsync(string itemId, CancellationToken cancellationToken) =>
        http.GetJsonAsync<List<ExternalIdInfo>>(
            Url($"Items/{itemId}/ExternalIdInfos", ("IsSupportedAsIdentifier", "true")),
            Context,
            cancellationToken);

    /// <summary>
    /// 按名字／年份／各家标识符去刮削源找候选。<paramref name="itemType"/> 是条目的类型原文（<c>Movie</c>、
    /// <c>Series</c>…）—— 服务器的路由就是按它分的，所以发之前先过 <see cref="ItemIdentify.Supports"/>。
    /// <para>
    /// 服务器答空数组是常事（名字对不上、插件没装、刮削源不通），那不是错。
    /// </para>
    /// </summary>
    public Task<List<RemoteSearchResult>> SearchRemoteAsync(
        string itemType,
        RemoteSearchQuery query,
        CancellationToken cancellationToken) =>
        http.PostJsonAsync<List<RemoteSearchResult>>(
            Url($"Items/RemoteSearch/{Uri.EscapeDataString(itemType)}"),
            query,
            Context,
            cancellationToken);

    /// <summary>
    /// 把挑中的那一条落在这个条目上：服务器据此改写标识符、按它重新刮一遍元数据。管理员才能用。
    /// <para>
    /// <paramref name="replaceAllImages"/> 是网页端那个「替换现有图像」开关。默认<b>不</b>换 —— 用户刚在
    /// 「修改媒体封面图」里挑的那张封面，不该因为一次识别被扔掉。交回去的
    /// <see cref="RemoteSearchResult"/> 必须是服务器发来的原样那一条（见那个类的注释）。
    /// </para>
    /// </summary>
    public Task ApplyRemoteSearchResultAsync(
        string itemId,
        RemoteSearchResult result,
        bool replaceAllImages,
        CancellationToken cancellationToken) =>
        http.PostAsync(
            Url($"Items/RemoteSearch/Apply/{itemId}",
                ("ReplaceAllImages", replaceAllImages ? "true" : "false")),
            result,
            Context,
            cancellationToken);

    /// <summary>
    /// 取一张候选海报的字节。走的是服务器那条转发（<c>Items/RemoteSearch/Image</c>），不是直连刮削源 ——
    /// 本机连不上 TMDB 的 CDN 是常事，而服务器多半连得上。
    /// </summary>
    public Task<byte[]> GetRemoteSearchImageAsync(
        string imageUrl,
        string? providerName,
        CancellationToken cancellationToken) =>
        http.GetBytesAsync(
            Url("Items/RemoteSearch/Image",
                ("imageUrl", imageUrl), ("ProviderName", providerName)),
            Context,
            cancellationToken);

    /// <summary>
    /// 移除识别：把服务器上这个条目的元数据整个重置掉，之后它不再对上任何一部片子。
    /// <para>
    /// <b>不可撤销</b> —— 手改过的片名、简介、分级一并没了，所以调用方必须先问过用户（网页端也是先弹一句
    /// 「ResetMetadataConfirmation」，而且那颗默认键是「取消」）。
    /// </para>
    /// <para>
    /// <c>ItemIds</c> 两处都给：官方 REST 文档写的是查询串（<c>?ItemIds=…</c>），而官方网页端实际发的是
    /// 一个 JSON 正文（<c>{"ItemIds":"…"}</c>）。两处都给，服务器认哪一处都成立 —— 这一位点错了的代价是
    /// 「按下去什么都没发生」，那比多发几个字节难查得多。
    /// </para>
    /// </summary>
    public Task ResetMetadataAsync(string itemId, CancellationToken cancellationToken) =>
        http.PostAsync(
            Url("Items/Metadata/Reset", ("ItemIds", itemId)),
            new { ItemIds = itemId },
            Context,
            cancellationToken);

    /// <summary>
    /// 删除一个条目 —— <b>连磁盘上的文件一起删</b>，不可撤销。调用方必须先问过用户（见
    /// <c>ItemCommands</c> 那一句确认）。账号没有删除权限时服务器答 403，那是要报给用户的话。
    /// </summary>
    public Task DeleteItemAsync(string itemId, CancellationToken cancellationToken) =>
        http.DeleteAsync(Url($"Items/{itemId}"), Context, cancellationToken);

    // ---- playback reporting ---------------------------------------------------

    public Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken cancellationToken) =>
        Connection.IsFnNas ? FnNasMedia.ReportPlaybackAsync(report, cancellationToken)
        : Connection.IsPlex ? PlexMedia.ReportPlaybackStartAsync(report, cancellationToken)
        : http.PostAsync(Url("Sessions/Playing"), report, Context, cancellationToken);

    public Task ReportPlaybackProgressAsync(PlaybackReport report, CancellationToken cancellationToken) =>
        Connection.IsFnNas ? FnNasMedia.ReportPlaybackAsync(report, cancellationToken)
        : Connection.IsPlex ? PlexMedia.ReportPlaybackProgressAsync(report, cancellationToken)
        : http.PostAsync(Url("Sessions/Playing/Progress"), report, Context, cancellationToken);

    public Task ReportPlaybackStoppedAsync(PlaybackReport report, CancellationToken cancellationToken) =>
        Connection.IsFnNas ? FnNasMedia.ReportPlaybackAsync(report, cancellationToken)
        : Connection.IsPlex ? PlexMedia.ReportPlaybackStoppedAsync(report, cancellationToken)
        : http.PostAsync(Url("Sessions/Playing/Stopped"), report, Context, cancellationToken);

    // ---- notifications ---------------------------------------------------------
    //
    // 「把本项目的通知改为 emby 的，让 emby 负责转发」（用户令 2026-09-25）：通知条目的增删改查走服务器 4.8+
    // 那套「用户通知」接口 —— 就是官方网页端 /settings/notifications.html 用的四条路。四条都带 UserId 查询
    // 参数：官方网页端就是这么问的，而且实测不带它时 Defaults 一类端点直接空引用 500。

    /// <summary>装在服务器上的通知服务（通知渠道）。这台服务器上装了官方 Webhooks 插件时，答案就是它。</summary>
    public Task<List<NotificationServiceInfo>> GetNotificationServicesAsync(CancellationToken cancellationToken) =>
        http.GetJsonAsync<List<NotificationServiceInfo>>(
            EmbyOnlyUrl("Notifications/Services", ("UserId", Connection.UserId)), Context, cancellationToken);

    /// <summary>可订阅的事件清单，按类分节。名字服务器已按用户的语言本地化，界面上直接用，不再自带一份词表。</summary>
    public Task<List<NotificationCategoryInfo>> GetNotificationTypesAsync(CancellationToken cancellationToken) =>
        http.GetJsonAsync<List<NotificationCategoryInfo>>(
            EmbyOnlyUrl("Notifications/Types", ("UserId", Connection.UserId)), Context, cancellationToken);

    /// <summary>当前用户已配置的通知条目（「通知」页的列表本体）。</summary>
    public Task<List<UserNotificationInfo>> GetNotificationsAsync(CancellationToken cancellationToken) =>
        http.GetJsonAsync<List<UserNotificationInfo>>(
            EmbyOnlyUrl("Notifications/Services/Configured", ("UserId", Connection.UserId)),
            Context, cancellationToken);

    /// <summary>一条新通知的铺底（官方网页端「添加」的起点）：服务键、启用状态和该服务自己的默认选项，
    /// 事件表是空的 —— 事件由编辑器选。服务键取自 <see cref="NotificationServiceInfo.Id"/>。</summary>
    public Task<UserNotificationInfo> GetNotificationDefaultsAsync(string notifierKey, CancellationToken cancellationToken) =>
        http.GetJsonAsync<UserNotificationInfo>(
            EmbyOnlyUrl("Notifications/Services/Defaults",
                ("UserId", Connection.UserId), ("NotifierKey", notifierKey)), Context, cancellationToken);

    /// <summary>保存一条通知（新建与编辑同一条路，整份 POST —— 官方网页端就是这个节拍）。</summary>
    public Task SaveNotificationAsync(UserNotificationInfo entry, CancellationToken cancellationToken) =>
        http.PostAsync(EmbyOnlyUrl("Notifications/Services/Configured"), entry, Context, cancellationToken);

    /// <summary>删一条通知。官方网页端删的是「这条 + 这个用户」，两个参数都在查询串上。</summary>
    public Task DeleteNotificationAsync(string entryId, CancellationToken cancellationToken) =>
        http.DeleteAsync(EmbyOnlyUrl("Notifications/Services/Configured",
            ("Id", entryId), ("UserId", Connection.UserId)), Context, cancellationToken);

    /// <summary>让服务器按这条通知的配置真发一次。目的地不通、令牌不对时服务器把原因写进错误回应里，这里不吞。</summary>
    public Task TestNotificationAsync(UserNotificationInfo entry, CancellationToken cancellationToken) =>
        http.PostAsync(EmbyOnlyUrl("Notifications/Services/Test"), entry, Context, cancellationToken);

    /// <summary>「限定用户」选择框的数据源（管理员才用得上，但读这四个名字没有权限门槛）。</summary>
    public Task<List<NotificationUser>> GetNotificationUsersAsync(CancellationToken cancellationToken) =>
        http.GetJsonAsync<List<NotificationUser>>(
            EmbyOnlyUrl("Users",
                ("SortBy", "SortName"), ("SortOrder", "Ascending"), ("EnableImages", "false")),
            Context, cancellationToken);

    /// <summary>「限定媒体库」选择框的数据源。</summary>
    public Task<List<NotificationLibrary>> GetNotificationLibrariesAsync(CancellationToken cancellationToken) =>
        http.GetJsonAsync<List<NotificationLibrary>>(
            EmbyOnlyUrl("Library/VirtualFolders",
                ("SortBy", "SortName"), ("SortOrder", "Ascending"), ("EnableImages", "false")),
            Context, cancellationToken);

    /// <summary>「限定设备」选择框的数据源。</summary>
    public async Task<List<NotificationDevice>> GetNotificationDevicesAsync(CancellationToken cancellationToken)
    {
        var list = await http.GetJsonAsync<NotificationDeviceList>(
            EmbyOnlyUrl("Devices"), Context, cancellationToken).ConfigureAwait(false);
        return list.Items;
    }

    // ---- server dashboard ------------------------------------------------------

    /// <summary>
    /// What the server says about itself. <c>/System/Info</c> is administrators-only, so a normal
    /// account falls back to the public subset — same field names, fewer of them — with
    /// <see cref="EmbySystemInfo.IsRestricted"/> set so the page can say why the ports are missing.
    /// A 401 is deliberately not caught here: that is an expired token, and the session has to see it.
    /// </summary>
    public async Task<EmbySystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) return await FnNasMedia.GetSystemInfoAsync(Connection.ServerName, Connection.ServerVersion, cancellationToken).ConfigureAwait(false);
        if (Connection.IsPlex) return await PlexMedia.GetSystemInfoAsync(Connection.ServerName, Connection.ServerVersion, cancellationToken).ConfigureAwait(false);
        if (Connection.IsJellyfin)
        {
            var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
            var restricted = user.Policy?.IsAdministrator != true;
            var info = await http.GetJsonAsync<EmbySystemInfo>(
                Url(restricted ? "System/Info/Public" : "System/Info"), Context, cancellationToken).ConfigureAwait(false);
            info.IsRestricted = restricted;
            return info;
        }
        try
        {
            return await http
                .GetJsonAsync<EmbySystemInfo>(Url("System/Info"), Context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EmbyApiException error) when (error.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Log.Debug(Category, "当前账户不是管理员，改用公开的服务器信息");
            var info = await http
                .GetJsonAsync<EmbySystemInfo>(Url("System/Info/Public"), Context, cancellationToken)
                .ConfigureAwait(false);
            info.IsRestricted = true;
            return info;
        }
    }

    // ---- media ---------------------------------------------------------------

    public Task<byte[]> GetImageBytesAsync(string itemId, string imageType, string? tag, int maxWidth, CancellationToken cancellationToken,
        int? index = null)
    {
        if (Connection.IsFnNas) return FnNasMedia.GetImageAsync(tag, maxWidth, cancellationToken);
        if (Connection.IsPlex) return PlexMedia.GetImageAsync(tag, maxWidth, cancellationToken);
        var url = EmbyUrl.Image(ApiBase, itemId, imageType, tag, maxWidth, index);
        return http.GetBytesAsync(url, Context, cancellationToken);
    }

    /// <summary>The still for one chapter of an item; <paramref name="index"/> is its position in <c>Chapters</c>.</summary>
    public Task<byte[]> GetChapterImageBytesAsync(string itemId, int index, string? tag, int maxWidth, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas) throw new NotSupportedException("飞牛影视不提供章节缩略图");
        if (Connection.IsPlex) throw new NotSupportedException("Plex 不提供章节缩略图");
        var url = EmbyUrl.ChapterImage(ApiBase, itemId, index, tag, maxWidth);
        return http.GetBytesAsync(url, Context, cancellationToken);
    }

    public Uri StreamUrl(EmbyItem item, MediaSource? source) =>
        Connection.IsFnNas ? Momoka.FnNas.FnNasPlaybackSelection.MediaUrl(Connection, source)
        : Connection.IsPlex ? Momoka.Plex.PlexPlaybackSelection.MediaUrl(Connection.ApiBase, source)
        : EmbyUrl.Stream(ApiBase, item.Id, source?.Id, source?.Container);

    public Uri SubtitleUrl(EmbyItem item, MediaSource source, MediaStream subtitle) =>
        Connection.IsFnNas ? throw new NotSupportedException("飞牛影视不支持此外挂字幕操作")
        : Connection.IsPlex ? Momoka.Plex.PlexPlaybackSelection.SubtitleUrl(Connection.ApiBase, subtitle)
        : EmbyUrl.Subtitle(ApiBase, item.Id, source.Id, subtitle.Index, subtitle.Codec);

    // ---- 封面图与字幕 ---------------------------------------------------------

    /// <summary>
    /// 「修改媒体封面图」的候选：各家刮削源上这个条目有哪些这一种图。
    /// <para>
    /// <c>IncludeAllLanguages</c> 开着 —— 中文封面在服务器的默认语言之外，关着的话这台服务器上常常一张都
    /// 列不出来。上限 60 张：多到足够挑，又还是一趟请求。
    /// </para>
    /// </summary>
    public Task<RemoteImageResult> GetRemoteImagesAsync(
        string itemId,
        string imageType,
        CancellationToken cancellationToken) =>
        http.GetJsonAsync<RemoteImageResult>(
            Url($"Items/{itemId}/RemoteImages",
                ("Type", imageType),
                ("IncludeAllLanguages", "true"),
                ("Limit", "60")),
            Context,
            cancellationToken);

    /// <summary>挑中的那一张交给服务器去取并挂到条目上。管理员才能用。</summary>
    public Task ApplyRemoteImageAsync(
        string itemId,
        string imageType,
        string imageUrl,
        string? providerName,
        CancellationToken cancellationToken) =>
        http.PostAsync(
            Url($"Items/{itemId}/RemoteImages/Download",
                ("Type", imageType),
                ("ProviderName", providerName),
                ("ImageUrl", imageUrl)),
            null,
            Context,
            cancellationToken);

    /// <summary>
    /// 候选图的缩略图，<b>由服务器代取</b>（<c>/Images/Remote</c>）。不让客户端直接去连刮削源：那些地址在
    /// 国内多半连不上，而服务器既然能列出这一批，就一定连得上。
    /// </summary>
    public Task<byte[]> GetRemoteImageBytesAsync(string imageUrl, CancellationToken cancellationToken) =>
        http.GetBytesAsync(
            Url("Images/Remote", ("ImageUrl", imageUrl)),
            Context,
            cancellationToken);

    /// <summary>
    /// 把服务器上这个条目的一种图删掉。<b>不可撤销</b> —— 服务器不会替你留着备份，调用方必须先问过用户。
    /// <para>
    /// <paramref name="index"/> 只对背景图那种「一个条目一列」的图种有意义（Emby 的地址
    /// <c>/Items/{id}/Images/{type}/{index}</c> 是它本来的形状）；其余几种一个条目各只有一张，index 传 null，
    /// 地址就是不带序号的短形式。传错了不会出错 —— 服务器按序号取，越界答 404。
    /// </para>
    /// <para>
    /// 删掉的可能正是界面上某一处正在用的图（剧名上方那枚徽标、页尾那条横幅、详情页背后那张背景图），所以
    /// 调用方删完必须让那一页重读：<see cref="EmbyItem.ImageTags"/> 上那个标签跟着就没了，而重读才知道。
    /// </para>
    /// </summary>
    public Task DeleteImageAsync(
        string itemId,
        string imageType,
        int? index,
        CancellationToken cancellationToken) =>
        http.DeleteAsync(
            Url(ImagePath(itemId, imageType, index)),
            Context,
            cancellationToken);

    /// <summary>
    /// 从本机传一张图上去，设成这个条目的这一种图。
    /// <para>
    /// 正文就是图片的原样字节（<see cref="EmbyHttp.PostBytesAsync"/>），不套 JSON —— Emby 这个接口收的就是
    /// 一张图本身。<paramref name="contentType"/> 交给服务器去认，所以调用方要按后缀名说清是
    /// <c>image/jpeg</c> 还是 <c>image/png</c>：说成 <c>application/octet-stream</c> 也能成，但服务器的
    /// 图片处理那一头是按它选解码器的。
    /// </para>
    /// <para>
    /// 和 <see cref="ApplyRemoteImageAsync"/> 是两条路：那一条是「服务器，你去刮削源把这张取回来」，这一条是
    /// 「这一张，从我手上收下」。管理员才能用。
    /// </para>
    /// </summary>
    public Task UploadImageAsync(
        string itemId,
        string imageType,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken) =>
        http.PostBytesAsync(
            Url($"Items/{itemId}/Images/{imageType}"),
            Connection.IsJellyfin ? System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes)) : bytes,
            contentType,
            Context,
            cancellationToken);

    /// <summary>
    /// 一种图在服务器上那个地址的尾部（<c>Items/{id}/Images/{type}</c>，背景图多一个序号）。
    /// </summary>
    internal static string ImagePath(string itemId, string imageType, int? index) =>
        index is { } position
            ? $"Items/{itemId}/Images/{imageType}/{position}"
            : $"Items/{itemId}/Images/{imageType}";

    /// <summary>
    /// 按语言搜一遍这个文件的字幕。<paramref name="language"/> 是三字母代码（<c>chi</c>、<c>eng</c>），
    /// 服务器把它交给装着的字幕插件。一个插件都没装的时候答的是空数组，不是错。
    /// </summary>
    public Task<List<RemoteSubtitleInfo>> SearchSubtitlesAsync(
        string itemId,
        string mediaSourceId,
        string language,
        CancellationToken cancellationToken) =>
        SearchSubtitlesAsync(itemId, mediaSourceId, new ServerSubtitleSearch(language), cancellationToken);

    /// <summary>
    /// 与当前网页端一样，仅强制字幕开启时发送 true，关闭时省略参数，不排除普通或强制字幕。
    /// Jellyfin 的字幕搜索没有 IsForced，不能发一个服务器会静默忽略的开关。
    /// </summary>
    public Task<List<RemoteSubtitleInfo>> SearchSubtitlesAsync(
        string itemId,
        string mediaSourceId,
        ServerSubtitleSearch search,
        CancellationToken cancellationToken)
    {
        if (Connection.IsJellyfin && search.ForcedOnly)
            throw new NotSupportedException("Jellyfin 不支持仅强制字幕搜索");
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(search.Language);
        return http.GetJsonAsync<List<RemoteSubtitleInfo>>(
            Url($"Items/{SubtitleItemId(itemId)}/RemoteSearch/Subtitles/{Uri.EscapeDataString(search.Language)}",
                ("MediaSourceId", mediaSourceId),
                ("IsForced", search.ForcedOnly ? "true" : null)),
            Context,
            cancellationToken);
    }

    private static string SubtitleItemId(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (itemId is "." or ".." || itemId.IndexOfAny(['/', '\\', '?', '#']) >= 0)
            throw new ArgumentException("字幕操作缺少有效的条目标识", nameof(itemId));
        return Uri.EscapeDataString(itemId);
    }

    /// <summary>
    /// 下载一条字幕挂到这个文件上，答案是它成了第几条轨道。
    /// <para>
    /// 那个 id 要转义：它是字幕插件自己造的记号，里头出现斜杠的话，不转义就把路由整条打断。
    /// </para>
    /// </summary>
    public Task<SubtitleDownloadResult?> DownloadSubtitleAsync(
        string itemId,
        string mediaSourceId,
        string subtitleId,
        CancellationToken cancellationToken) =>
        http.PostForJsonAsync<SubtitleDownloadResult>(
            Url($"Items/{SubtitleItemId(itemId)}/RemoteSearch/Subtitles/{Uri.EscapeDataString(subtitleId)}",
                ("MediaSourceId", mediaSourceId)),
            null,
            Context,
            cancellationToken);

    /// <summary>删掉一条外挂字幕。内嵌在容器里的那些删不掉，服务器会拒绝。</summary>
    public Task DeleteSubtitleAsync(string itemId, int index, CancellationToken cancellationToken) =>
        http.DeleteAsync(Url($"{(Connection.IsJellyfin ? "Videos" : "Items")}/{SubtitleItemId(itemId)}/Subtitles/{index}"), Context, cancellationToken);

    // ---- 下载到设备 -----------------------------------------------------------

    /// <summary>
    /// 把这个条目的原始文件下到 <paramref name="path"/>，返回一共多少字节。走的是服务器的下载接口，也就是
    /// 原封不动的那个文件（不转码）。账号没有下载权限时服务器答 403。
    /// </summary>
    public Task<long> DownloadToFileAsync(
        string itemId,
        string path,
        IProgress<(long Done, long? Total)>? progress,
        CancellationToken cancellationToken) =>
        http.DownloadToFileAsync(
            Url($"Items/{itemId}/Download"),
            path,
            Context,
            progress,
            cancellationToken);
}
