using System.Text.Json;
using System.Text.Json.Serialization;

namespace Momoka.Emby;

public sealed class ItemsResult
{
    public List<EmbyItem> Items { get; set; } = [];

    public int TotalRecordCount { get; set; }
}

/// <summary>
/// One row of <c>GET /Movies/Recommendations</c>. Jellyfin groups the films by why they were suggested
/// (<c>SimilarToRecentlyPlayed</c>, <c>HasLikedActor</c>…) and supplies the name of the film the reason
/// is based on; the web client turns those two fields into a section title.
/// </summary>
public sealed class MovieRecommendation
{
    public List<EmbyItem> Items { get; set; } = [];

    public string? RecommendationType { get; set; }

    public string? BaselineItemName { get; set; }

    public string? CategoryId { get; set; }
}

public sealed class EmbyUser
{
    public EmbyUserPolicy? Policy { get; set; }
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? ServerId { get; set; }

    public bool HasPassword { get; set; }

    public string? PrimaryImageTag { get; set; }
}

public sealed class EmbyUserPolicy
{
    public bool IsAdministrator { get; set; }

    /// <summary>
    /// 用户策略里的「允许下载媒体」。可空：旧版本或登录响应没带这一项时是「还没问到」，不是「不许下载」——
    /// 未知按可用处理，真没权限由服务器拒绝。
    /// </summary>
    public bool? EnableContentDownloading { get; set; }

    /// <summary>用户策略里的「允许删除媒体」。菜单据此收起删除入口。</summary>
    public bool? EnableContentDeletion { get; set; }

    /// <summary>用户策略里的「允许下载字幕」。</summary>
    public bool? EnableSubtitleDownloading { get; set; }

    /// <summary>用户策略里的「允许管理字幕」。</summary>
    public bool? EnableSubtitleManagement { get; set; }

    /// <summary>
    /// 把这一份策略写进连接快照。只写能确认的值：可空策略字段仍为 null 时表示服务器没答，菜单按未知处理。
    /// </summary>
    public EmbyConnection ApplyTo(EmbyConnection connection)
    {
        var permissions = ToPermissions();
        return connection with
        {
            IsAdministrator = permissions.IsAdministrator,
            CanDownload = permissions.CanDownload,
            CanDeleteContent = permissions.CanDeleteContent,
            CanManageSubtitles = permissions.CanManageSubtitles
        };
    }

    /// <summary>「更多」菜单用的账号能力。两个字幕策略位有一个为 true 就算能管；都没答时留 null。</summary>
    public ItemMenuPermissions ToPermissions() => new(
        IsAdministrator,
        EnableContentDownloading,
        EnableContentDeletion,
        EnableSubtitleDownloading == true || EnableSubtitleManagement == true
            ? true
            : EnableSubtitleDownloading == false && EnableSubtitleManagement == false ? false : null);
}

public sealed class AuthenticationResult
{
    public string AccessToken { get; set; } = "";

    public string? ServerId { get; set; }

    public EmbyUser? User { get; set; }
}

public sealed class PublicSystemInfo
{
    public string? ProductName { get; set; }

    [JsonIgnore]
    public MediaServerKind? DetectedServerKind { get; set; }

    [JsonIgnore]
    public MediaServerKind ServerKind => DetectedServerKind ?? (IsJellyfinProductName(ProductName)
        ? MediaServerKind.Jellyfin : MediaServerKind.Emby);

    /// <summary>
    /// Jellyfin 的公开系统信息报的是 <c>"Jellyfin Server"</c>（10.10.7 实测），不是产品简称；
    /// 只认全等会把整台服务器当成 Emby，管理入口和网页皮肤就全走错。产品名以 Jellyfin 开头即可。
    /// </summary>
    private static bool IsJellyfinProductName(string? productName) =>
        productName?.Trim().StartsWith("Jellyfin", StringComparison.OrdinalIgnoreCase) == true;

    [JsonIgnore]
    public Uri? ApiBase { get; set; }
    public string? ServerName { get; set; }

    public string? Version { get; set; }

    public string? Id { get; set; }

    public string? OperatingSystem { get; set; }
}

/// <summary>
/// What the 服务器 page reads out of <c>/System/Info</c>. That endpoint is administrators-only; a
/// normal account gets the public subset instead, which uses the same field names, so one class
/// covers both and <see cref="IsRestricted"/> records which one answered.
/// </summary>
public sealed class EmbySystemInfo
{
    public string? ServerName { get; set; }

    public string? Version { get; set; }

    public string? Id { get; set; }

    public string? OperatingSystem { get; set; }

    public string? OperatingSystemDisplayName { get; set; }

    public string? PackageName { get; set; }
    public string? ProgramDataPath { get; set; }
    public string? CachePath { get; set; }
    public string? LogPath { get; set; }
    public string? InternalMetadataPath { get; set; }
    public string? TranscodingTempPath { get; set; }
    public bool? CanSelfRestart { get; set; }
    public bool IsShuttingDown { get; set; }

    /// <summary>The port the server answers plain HTTP on; admin-only.</summary>
    public int? HttpServerPortNumber { get; set; }

    public int? HttpsPortNumber { get; set; }

    public bool EnableHttps { get; set; }

    /// <summary>"http://172.17.0.4:8096" — the address to hand to another device on the same network.</summary>
    public string? LocalAddress { get; set; }

    /// <summary>"http://67.159.48.146:8096" — only set when the server knows its public address.</summary>
    public string? WanAddress { get; set; }

    public bool HasUpdateAvailable { get; set; }

    public bool HasPendingRestart { get; set; }

    public string? SystemUpdateLevel { get; set; }

    /// <summary>Set by the client when only the public subset came back, so the page can explain the gaps.</summary>
    [JsonIgnore]
    public bool IsRestricted { get; set; }
}

/// <summary>Everything the app needs to talk to one server as one user.</summary>
public sealed record EmbyConnection(
    Uri ApiBase,
    string AccessToken,
    string UserId,
    string UserName,
    string ServerName,
    DeviceIdentity Device)
{
    public MediaServerKind ServerKind { get; init; }
    public string? ServerVersion { get; init; }
    public string? ServerId { get; init; }
    public bool? IsAdministrator { get; init; }

    /// <summary>
    /// 这个账号能不能下载媒体（用户策略里的 <c>EnableContentDownloading</c>）。null 表示还没问到：菜单照旧
    /// 摆出下载入口，真没权限时由服务器 403 拒绝；只有服务器明确答 false 才把它收起。
    /// <para>
    /// 整部剧、整季的下载要下里头的每一集，而服务器给剧和季本身回的 <c>CanDownload</c> 是 false（Jellyfin
    /// 实测如此），所以入口的显隐只能看账号策略，不能看条目上的那一位。
    /// </para>
    /// </summary>
    public bool? CanDownload { get; init; }

    /// <summary>账号能不能删媒体（用户策略里的 <c>EnableContentDeletion</c>）；null 表示还没问到。</summary>
    public bool? CanDeleteContent { get; init; }

    /// <summary>
    /// 账号能不能管理/下载字幕（<c>EnableSubtitleDownloading</c> 或 <c>EnableSubtitleManagement</c>）。
    /// 两个策略位都没答时是 null。
    /// </summary>
    public bool? CanManageSubtitles { get; init; }

    public bool IsJellyfin => ServerKind == MediaServerKind.Jellyfin;
    public bool IsFnNas => ServerKind == MediaServerKind.FnNas;
    public bool IsPlex => ServerKind == MediaServerKind.Plex;

    /// <summary>Plex 没有收藏列表；收藏页与账号筛选不把它算作来源。</summary>
    public bool SupportsFavorites => !IsPlex;

    /// <summary>
    /// 跨库题材页（详情页那一行类型点进去的「这个类型下的全部影片和剧集」）目前只有 Emby 与 Jellyfin 有路：
    /// Plex 客户端未实现，飞牛的适配器对 <c>Genre</c> 条件直接抛 <see cref="NotSupportedException"/>。
    /// </summary>
    public bool SupportsGenreBrowse => !IsPlex && !IsFnNas;

    /// <summary>
    /// Whether this product's routes and identity have to be re-probed before a saved token is trusted.
    /// All three non-Emby products do, and for the same reason: what was saved is a token and an address,
    /// and only the server can confirm that the two still belong together.
    /// </summary>
    public bool IsProbedProduct => IsJellyfin || IsFnNas || IsPlex;

    public bool UsesJellyfin12Routes => IsJellyfin && (!Version.TryParse(ServerVersion?.Split('-')[0], out var version) || version.Major >= 12);
    /// <summary>
    /// 这两条连接是不是「同一台服务器上的同一个人」。令牌不算：一次重新登录换的正是令牌，而换了令牌的还是他。
    /// <para>
    /// 这是 <c>EmbySession.ExecuteAsync</c> 判「令牌过期之后那一趟还能不能原样重发一遍」的判据。
    /// 会话可能在这中间换了主人：同一个人重新登录了，那这一趟接着走；换成别的账号、或者换了一台服务器，
    /// 那这一趟就属于一个已经不存在的身份，必须作废 —— 拿新身份把它重发一遍，是把上一个账号的「标记已观看」
    /// 记到下一个账号头上，或者把一个条目 id 打到另一台根本没有这个 id 的服务器上。
    /// </para>
    /// <para>
    /// 服务器和人要一起比，少一样都不够：同一台服务器上换个人、和同一个人换台服务器，都是换了身份。
    /// </para>
    /// </summary>
    public bool IsSameIdentityAs(EmbyConnection other) =>
        ServerKind == other.ServerKind && ApiBase == other.ApiBase && string.Equals(UserId, other.UserId, StringComparison.Ordinal)
        && (string.IsNullOrEmpty(ServerId) || string.IsNullOrEmpty(other.ServerId) || string.Equals(ServerId, other.ServerId, StringComparison.Ordinal));
}

/// <summary>新建合集之后服务器答的那一句：这个合集的 id 和名字。</summary>
public sealed class CollectionCreationResult
{
    public string Id { get; set; } = "";

    public string? Name { get; set; }
}

/// <summary>
/// 「修改媒体封面图」问服务器要来的那一批候选图：各家刮削源上这个条目有哪些封面。
/// <see cref="Providers"/> 是这个条目能问的几家，一家都没有的时候那一批也是空的（服务器没装刮削插件、
/// 或者这个条目没有对上任何一家的记录）。
/// </summary>
public sealed class RemoteImageResult
{
    public List<RemoteImageInfo> Images { get; set; } = [];

    public int TotalRecordCount { get; set; }

    public List<string> Providers { get; set; } = [];
}

/// <summary>候选图中的一张。</summary>
public sealed class RemoteImageInfo
{
    public string? ProviderName { get; set; }

    /// <summary>原图地址。下载那一下交回给服务器，由服务器去取（客户端不一定连得上刮削源）。</summary>
    public string Url { get; set; } = "";

    /// <summary>缩略图地址，多数刮削源会给；没给就退回 <see cref="Url"/>。</summary>
    public string? ThumbnailUrl { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? CommunityRating { get; set; }

    public int? VoteCount { get; set; }

    public string? Language { get; set; }

    public string? DisplayLanguage { get; set; }

    /// <summary>"Primary"、"Backdrop"…… 问的时候点了名，所以回来的都是那一种。</summary>
    public string? Type { get; set; }

    /// <summary>一行给人看的说明：哪家、多大、什么语言。</summary>
    public string Describe()
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(ProviderName)) parts.Add(ProviderName!);
        if (Width is > 0 && Height is > 0) parts.Add($"{Width}×{Height}");
        if (DisplayLanguage is { Length: > 0 } language) parts.Add(language);
        else if (Language is { Length: > 0 } code) parts.Add(code);
        if (CommunityRating is { } rating and > 0) parts.Add($"{rating:0.#} 分");
        return string.Join("  ·  ", parts);
    }
}

/// <summary>字幕搜索结果中的一条。</summary>
public sealed class RemoteSubtitleInfo
{
    /// <summary>下载时交回给服务器的那个记号，不是给人看的。</summary>
    public string Id { get; set; } = "";

    public string? ProviderName { get; set; }

    public string? Name { get; set; }

    public string? Format { get; set; }

    public string? Author { get; set; }

    public string? Comment { get; set; }

    public string? Language { get; set; }

    public float? CommunityRating { get; set; }

    public int? DownloadCount { get; set; }

    /// <summary>按文件哈希对上的 —— 这一条基本可以肯定是这个文件的字幕，而不是同名影片的另一个版本。</summary>
    public bool? IsHashMatch { get; set; }

    public bool? IsForced { get; set; }

    public bool? IsHearingImpaired { get; set; }

    /// <summary>列表里那一行：名字，加上能说清「这一条靠不靠得住」的几样。</summary>
    public string Describe()
    {
        var parts = new List<string>(6);
        if (!string.IsNullOrWhiteSpace(ProviderName)) parts.Add(ProviderName!);
        if (!string.IsNullOrWhiteSpace(Language)) parts.Add(Language!);
        if (!string.IsNullOrWhiteSpace(Format)) parts.Add(Format!.ToUpperInvariant());
        if (IsHashMatch == true) parts.Add("文件精确匹配");
        if (DownloadCount is { } count and > 0) parts.Add($"{count} 次下载");
        if (CommunityRating is { } rating and > 0) parts.Add($"{rating:0.#} 分");
        if (IsForced == true) parts.Add("强制");
        if (IsHearingImpaired == true) parts.Add("听障");
        return string.Join("  ·  ", parts);
    }
}

/// <summary>下载一条字幕之后服务器答的：它在这个文件里成了第几条轨道。</summary>
public sealed class SubtitleDownloadResult
{
    public int? NewIndex { get; set; }
}

/// <summary>
/// 一家刮削源在这个条目上能填的标识符（<c>GET /Items/{Id}/ExternalIdInfos</c> 的一条）。服务器按条目类型
/// 决定有哪几家 —— 电影是 IMDb / TheMovieDb / TheTVDB / 豆瓣，剧集是 TheTVDB / TheMovieDb / IMDb…
/// </summary>
public sealed class ExternalIdInfo
{
    /// <summary>显示名：「IMDb」「TheMovieDb」「TheTVDB」「豆瓣」。</summary>
    public string Name { get; set; } = "";

    /// <summary><c>ProviderIds</c> 里那个键（<c>Imdb</c> / <c>Tmdb</c> / <c>Tvdb</c> / <c>Douban</c>）。</summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// 这家刮削源的官网，界面上在每个输入框下面当链接显示（网页端就是这么做的）。服务器没给就是空 ——
    /// 那一格只有输入框，没有链接。
    /// </summary>
    public string? Website { get; set; }

    /// <summary>带占位符的网址模板；原生 Emby 元数据编辑器经 EmbyMetadataLink 转义并验证后使用。</summary>
    public string? UrlFormatString { get; set; }
}

/// <summary>
/// 刮削源上的一条候选（<c>POST /Items/RemoteSearch/{Type}</c> 的一条）。
/// <para>
/// <see cref="Extra"/> 不是装饰：提交（<c>POST /Items/RemoteSearch/Apply/{Id}</c>）要把服务器发来的
/// <b>原样那一条</b>交回去，服务器按里头的 <c>ProviderIds</c> 和 <c>SearchProviderName</c> 去认片子，
/// 少一样就可能认不出来。逐字段抄一遍等于承诺「服务器的字段表我全知道」，而那张表跟着版本走 ——
/// 没点名的原样留着，提交时自然带回去。
/// </para>
/// </summary>
public sealed class RemoteSearchResult
{
    public string? Name { get; set; }

    public Dictionary<string, string> ProviderIds { get; set; } = [];

    public int? ProductionYear { get; set; }

    public int? IndexNumber { get; set; }

    public int? ParentIndexNumber { get; set; }

    public string? PremiereDate { get; set; }

    /// <summary>
    /// 刮削源上那张海报。注意它多半是外网地址、本机不一定连得上 —— 所以取它走的是服务器那条
    /// <c>Items/RemoteSearch/Image</c> 转发（见 <c>EmbyClient.GetRemoteSearchImageAsync</c>），
    /// 网页端也是这么做的。
    /// </summary>
    public string? ImageUrl { get; set; }

    public string? ThumbnailUrl { get; set; }

    /// <summary>这一条是哪家给的（<c>TheMovieDb</c> / <c>IMDb</c>…），卡片上那一行小字就是它。</summary>
    public string? SearchProviderName { get; set; }

    public string? Overview { get; set; }

    /// <summary>服务器发来、上面没点名的字段，提交时原样带回去。见类注释。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>卡片上那一行小字：哪一家给的、哪一年。</summary>
    public string Describe()
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(SearchProviderName)) parts.Add(SearchProviderName!);
        if (ProductionYear is { } year and > 0) parts.Add(year.ToString());
        return string.Join("  ·  ", parts);
    }
}
