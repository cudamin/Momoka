using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>详情正文卡片目录与排序归一化；季页及其他类型始终沿用默认版面。</summary>
public static class DetailLayout
{
    public const string Episodes = "episodes";
    public const string Info = "info";
    public const string Seasons = "seasons";
    public const string Cast = "cast";
    public const string Similar = "similar";

    private static readonly string[] AllCards = [Episodes, Info, Seasons, Cast, Similar];
    private static readonly string[] MovieCards = [Info, Cast, Similar];
    private static readonly string[] EpisodeCards = [Episodes, Info, Cast, Similar];

    public static string Title(string key) => key switch
    {
        Episodes => "集列表",
        Info => "其他信息",
        Seasons => "全部剧季",
        Cast => "演职人员",
        Similar => "更多类似",
        _ => ""
    };

    /// <summary>
    /// 沿用有效存档的顺序与勾选，缺项默认显示并追加。返回新记录，不让版面计算修改用户的存档。
    /// </summary>
    public static IReadOnlyList<DetailCardSetting> Plan(string itemType, IEnumerable<DetailCardSetting>? saved)
    {
        var keys = itemType switch
        {
            EmbyItemType.Movie => MovieCards,
            EmbyItemType.Episode => EpisodeCards,
            _ => AllCards
        };
        var custom = itemType is EmbyItemType.Series or EmbyItemType.Episode or EmbyItemType.Movie;
        var available = keys.ToHashSet(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var plan = new List<DetailCardSetting>(keys.Length);

        foreach (var row in custom ? saved ?? [] : [])
        {
            if (row?.Key is not { Length: > 0 } key || !available.Contains(key) || !taken.Add(key)) continue;
            plan.Add(new DetailCardSetting { Key = key, Visible = row.Visible });
        }

        foreach (var key in keys)
            if (taken.Add(key)) plan.Add(new DetailCardSetting { Key = key });

        return plan;
    }

    /// <summary>每个页面只读取属于自己的偏好；季页等其他类型始终保留默认版面。</summary>
    public static IReadOnlyList<DetailCardSetting> For(UiSettings ui, string itemType) =>
        Plan(itemType, itemType switch
        {
            EmbyItemType.Series => ui.SeriesCards,
            EmbyItemType.Episode => ui.EpisodeCards,
            EmbyItemType.Movie => ui.MovieCards,
            _ => null
        });
}
