using Momoka.Emby;

namespace Momoka.AniBt;

/// <summary>番剧名用于 Bangumi 搜索；集号只用于选定作品后的 RSS 精确筛选。Emby 季号不等于 Bangumi 作品编号。</summary>
public sealed record AniBtSearchQuery(string Title, int? Episode)
{
    public static AniBtSearchQuery From(EmbyItem item, EmbyItem? owner = null)
    {
        var title = item.Type is EmbyItemType.Season or EmbyItemType.Episode
            ? new[] { item.SeriesName, owner?.SeriesName, owner?.Name, item.Name }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? ""
            : item.Name;
        return new(title.Trim(), item.Type == EmbyItemType.Episode ? item.IndexNumber : null);
    }
}
