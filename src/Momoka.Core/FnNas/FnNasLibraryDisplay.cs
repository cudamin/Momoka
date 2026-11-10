using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>
/// 官方网页 0.9.8 Hq/AN：其他与直播库及分类页、目录可切列表；普通电影/电视只选海报。
/// 目录只切列表/海报墙，不提供横竖切换。布局保存于本地，不写服务器 userData。
/// </summary>
public static class FnNasLibraryDisplay
{
    public static IReadOnlyList<LibraryView> Views(string? collectionType, bool isFolder) => isFolder
        ? [LibraryView.Poster, LibraryView.List]
        : collectionType is "homevideos" or "livetv"
            ? [LibraryView.Poster, LibraryView.Thumb, LibraryView.List]
            : [LibraryView.Poster, LibraryView.Thumb];

    /// <summary>只适配当前显示，不覆盖保存的首选；适用条件恢复后首选列表自然恢复。</summary>
    public static LibraryDisplay Fit(LibraryDisplay preference, string? collectionType, bool isFolder)
    {
        var display = preference.Normalized();
        display.FnNasPosterView = Poster(preference);
        display.View = EffectiveView(preference, collectionType, isFolder);
        return display;
    }

    public static LibraryView EffectiveView(LibraryDisplay preference, string? collectionType, bool isFolder)
    {
        if (preference.View == LibraryView.List && (isFolder || collectionType is "homevideos" or "livetv")) return LibraryView.List;
        if (isFolder) return LibraryView.Poster;
        return preference.View is LibraryView.Poster or LibraryView.Thumb ? preference.View : Poster(preference);
    }

    public static LibraryDisplay Choose(LibraryDisplay preference, LibraryView view, string? collectionType, bool isFolder)
    {
        if (!Views(collectionType, isFolder).Contains(view)) return preference.Clone();
        var display = preference.Normalized();
        display.FnNasPosterView = view is LibraryView.Poster or LibraryView.Thumb ? view : Poster(preference);
        display.View = view;
        if (view != LibraryView.List)
            display.CardSize = view == LibraryView.Thumb ? LibraryCardSize.Smaller : LibraryCardSize.Default;
        return display;
    }

    private static LibraryView Poster(LibraryDisplay preference) =>
        preference.FnNasPosterView is LibraryView.Poster or LibraryView.Thumb ? preference.FnNasPosterView.Value
            : preference.View == LibraryView.Thumb ? LibraryView.Thumb : LibraryView.Poster;
}
