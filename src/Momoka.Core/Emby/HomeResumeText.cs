namespace Momoka.Emby;

/// <summary>首页继续观看：剧名在上，季集与集名在下；不改变其它货架的卡片文案。</summary>
public static class HomeResumeText
{
    public static string Title(EmbyItem item) =>
        item.Type == EmbyItemType.Episode && !string.IsNullOrWhiteSpace(item.SeriesName)
            ? item.SeriesName.Trim()
            : item.Name;

    public static string Subtitle(EmbyItem item) =>
        item.Type == EmbyItemType.Episode
            ? LibraryRecommended.Subtitle(item) ?? ""
            : item.CardSubtitle;
}
