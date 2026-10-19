using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>
/// 飞牛网页端排序菜单的四项，顺序与用词照官方 0.9.8：添加日期、发行日期、标题、评分。值仍用
/// <see cref="EmbySortBy"/> 的常量，因为查询只认这四个键（见 <see cref="FnNasMediaAdapter.SupportsSort"/>）；
/// 界面上的字是飞牛自己的，不借用 Emby 那份「加入日期／IMDb 评分」的措辞。
/// </summary>
public static class FnNasSortCatalog
{
    public static IReadOnlyList<EmbySortOption> Options { get; } =
    [
        new() { Label = "添加日期", Value = EmbySortBy.DateAdded, DescendingByDefault = true },
        new() { Label = "发行日期", Value = EmbySortBy.ReleaseDate, DescendingByDefault = true },
        new() { Label = "标题", Value = EmbySortBy.Name },
        new() { Label = "评分", Value = EmbySortBy.Rating, DescendingByDefault = true }
    ];

    public static EmbySortOption Default => Options[0];

    public static EmbySortOption? Find(string value) => Options.FirstOrDefault(option => option.Value == value);
}
