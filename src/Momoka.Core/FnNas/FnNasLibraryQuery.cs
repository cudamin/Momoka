using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>
/// 飞牛媒体库页的身份，取自官方前端 0.9.8 的 <c>/library/{guid}</c>、<c>/list/all</c> 与
/// <c>/list/other</c>：自定义库页除非分类是 <c>Others</c>，一律按 <c>All</c> 处理 ——
/// <c>tags.type</c> 收电影、节目、文件夹、视频；<c>Others</c> 只问文件夹与视频。
/// 文件夹下钻页没有默认类型，<c>item/list</c> 改发 <c>parent_guid</c> 取直接子项。
/// </summary>
public static class FnNasLibraryQuery
{
    public const string All = "All";
    public const string Other = "Other";
    public const string Live = "LiveChannel";

    /// <summary>筛选面板的默认类型。文件夹页为空；「其他」库是 Other；其余自定义库是 All。</summary>
    public static string? DefaultType(string? collectionType, bool isFolder) =>
        isFolder ? null : collectionType == "homevideos" ? Other : All;

    /// <summary>
    /// <c>item/list</c> 的 <c>tags.type</c>。这些是本地标准类型名，由
    /// <see cref="FnNasMediaAdapter.Query"/> 换成飞牛的原生名（Series → TV、Folder → Directory）。
    /// <para>
    /// 除「其他」外，库根一律按官方 All 问电影、节目、文件夹、视频。飞牛的库分类不描述内容：
    /// 2026-11-01 实服上标成 Movie 的「混合影片」是 12 部剧 + 1 部电影 + 1 个文件夹，标成 TV 的
    /// 「电视节目」里还有 2 部电影和 7 个文件夹；照分类收窄会只剩一小半。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Types(string? collectionType) => collectionType switch
    {
        "homevideos" => [EmbyItemType.Folder, EmbyItemType.Video],
        "movies" or "tvshows" or "mixed" =>
            [EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Folder, EmbyItemType.Video],
        _ => []
    };

    /// <summary>
    /// <c>tag/list</c> 的 <c>type</c> 参数。官方只对「其他」问文件夹与视频；All 与文件夹页省略，
    /// 具体类型页（电影／电视节目／单集／直播）原样发自己。
    /// </summary>
    public static string? TagListType(string? defaultType) => defaultType switch
    {
        Other => "Directory,Video",
        "Movie" or "TV" or "Episode" or Live => defaultType,
        _ => null
    };
}
