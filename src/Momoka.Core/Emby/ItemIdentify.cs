namespace Momoka.Emby;

/// <summary>
/// 「识别」那一套：把一个条目对到刮削源的某一条记录上。
/// <para>
/// 摆在 Core 的理由和 <see cref="ItemMenu"/> 一样 —— 这里是「哪几种条目能识别」「表单上的字怎么写」
/// 「什么时候压根不该问服务器」这三件事的<b>唯一</b>说法，而它们错起来屏上都看着正常：给一季摆一条「识别」
/// （服务器的 <c>Items/RemoteSearch</c> 不认 <c>Season</c>，点下去只有 404）、或者一个条件都没填就发一趟
/// 空查询（刮削源回一屏不相干的东西，而用户以为自己填过）。
/// </para>
/// <para>
/// 和网页端同一套协议（Emby 网页端的 <c>components/itemidentifier</c>）：取标识符字段是
/// <c>GET /Items/{Id}/ExternalIdInfos</c>，搜索是 <c>POST /Items/RemoteSearch/{Type}</c>，落地是
/// <c>POST /Items/RemoteSearch/Apply/{Id}</c>，移除识别是 <c>POST /Items/Metadata/Reset</c>。
/// </para>
/// </summary>
public static class ItemIdentify
{
    /// <summary>输入框标签的后缀：「IMDb 标识符」。和网页端 <c>LabelDynamicExternalId</c> 同一句。</summary>
    public const string LabelSuffix = "标识符";

    /// <summary>
    /// 服务器的 <c>Items/RemoteSearch/{Type}</c> 认哪几种。**季和单集不在里头** —— 它们跟着所属的剧一起
    /// 识别，服务器没有「这一集单独对哪一部片子」这回事（官方网页端那三点的菜单里，剧集那一页也没有「识别」）。
    /// 文件夹、播放列表、媒体库同样没有可对上的片子。
    /// </summary>
    private static readonly HashSet<string> Identifiable = new(StringComparer.OrdinalIgnoreCase)
    {
        EmbyItemType.Movie,
        EmbyItemType.Series,
        EmbyItemType.BoxSet,
        EmbyItemType.Trailer,
        EmbyItemType.MusicVideo,
        EmbyItemType.MusicAlbum,
        EmbyItemType.MusicArtist
    };

    /// <summary>这个条目能不能识别 —— 「更多」菜单上那条「识别」看不看得到，全看这一位。</summary>
    public static bool Supports(EmbyItem item) => Identifiable.Contains(item.Type);

    /// <summary>一家刮削源的标识符输入框该写什么字。</summary>
    public static string FieldLabel(string providerName) => $"{providerName} {LabelSuffix}";

    /// <summary>
    /// 表单上一个条件都没填就别问。网页端在这里只提示一句、不发请求，这里照办：空查询打过去，刮削源答的是
    /// 「随便什么」，而那不是用户要找的东西 —— 一趟请求换一屏噪声。
    /// </summary>
    public static bool HasCriteria(string? name, IEnumerable<string?> ids) =>
        !string.IsNullOrWhiteSpace(name) || ids.Any(id => !string.IsNullOrWhiteSpace(id));

    /// <summary>
    /// 年份那一格：只认得出整数的才算填了。用户在年份里写「2026年」时当作没填 —— 这一位是交给刮削源做筛选的
    /// 数字，递一句它解析不了的话过去，换来的是一趟没有结果的请求。
    /// </summary>
    public static int? Year(string? text) =>
        int.TryParse(text?.Trim(), out var year) && year > 0 ? year : null;

    /// <summary>
    /// 按表单拼出搜索体。名字和年份留空就不发那一位（服务器自己区分「没填」和「填了空字符串」），
    /// 标识符留空的那几家同样不发。
    /// </summary>
    public static RemoteSearchQuery SearchQuery(
        EmbyItem item,
        string? name,
        string? year,
        IReadOnlyList<(string Key, string Value)> ids)
    {
        var info = new RemoteSearchInfo
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Year = Year(year)
        };

        foreach (var (key, value) in ids)
        {
            if (key.Length == 0 || string.IsNullOrWhiteSpace(value)) continue;
            info.ProviderIds[key] = value.Trim();
        }

        return new RemoteSearchQuery { ItemId = item.Id, SearchInfo = info };
    }
}

/// <summary>
/// 搜索体（<c>POST /Items/RemoteSearch/{Type}</c>）。形状照服务器的 <c>*InfoRemoteSearchQuery</c>：
/// 一条 <c>SearchInfo</c>，加要在哪个条目上找。
/// </summary>
public sealed class RemoteSearchQuery
{
    public RemoteSearchInfo SearchInfo { get; set; } = new();

    /// <summary>要在哪个条目上找。带上它，服务器会优先用这个条目所属媒体库的刮削源设置。</summary>
    public string? ItemId { get; set; }
}

/// <summary>搜索条件：标题、年份、各家刮削源的标识符。</summary>
public sealed class RemoteSearchInfo
{
    public string? Name { get; set; }

    public int? Year { get; set; }

    /// <summary>键是 <see cref="ExternalIdInfo.Key"/>（<c>Imdb</c> / <c>Tmdb</c> / <c>Tvdb</c>…）。</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = [];
}
