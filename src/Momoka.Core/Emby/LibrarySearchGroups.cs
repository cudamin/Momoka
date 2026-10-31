namespace Momoka.Emby;

/// <summary>搜索结果在「热门结果」里按条目类型分成的几组，次序固定。</summary>
public enum LibrarySearchGroupKind
{
    Series,
    Movie,
    Episode,
    Video
}

/// <summary>
/// 搜索结果的类型分组。Emby、Jellyfin 与飞牛的搜索都会把整库扫回的条目混在一份相关性清单里；
/// 页面上要按类型分开摆，分到哪一组、组叫什么名字由这里一处说清。
/// <para>
/// 只认 <see cref="ItemQuery.SweptTypes"/> 那四种类型。别的类型仍要有一格可去 —— 服务器多送一种不会让它
/// 从屏上消失，落进最后一组即可。这一条纯规则放在 Core，页面和视图模型都不各写一遍判据。
/// </para>
/// </summary>
public static class LibrarySearchGroups
{
    /// <summary>分组自上而下的次序，与搜索页「节目 / 影片 / 集」的标签次序一致。</summary>
    public static IReadOnlyList<LibrarySearchGroupKind> Order { get; } =
    [
        LibrarySearchGroupKind.Series,
        LibrarySearchGroupKind.Movie,
        LibrarySearchGroupKind.Episode,
        LibrarySearchGroupKind.Video
    ];

    public static LibrarySearchGroupKind KindOf(string? type) => type switch
    {
        EmbyItemType.Series => LibrarySearchGroupKind.Series,
        EmbyItemType.Movie => LibrarySearchGroupKind.Movie,
        EmbyItemType.Episode => LibrarySearchGroupKind.Episode,
        _ => LibrarySearchGroupKind.Video
    };

    /// <summary>组标题；「视频」那一组也收服务器送来的未知类型。</summary>
    public static string TitleOf(LibrarySearchGroupKind kind) => kind switch
    {
        LibrarySearchGroupKind.Series => "节目",
        LibrarySearchGroupKind.Movie => "影片",
        LibrarySearchGroupKind.Episode => "单集",
        _ => "视频"
    };
}
