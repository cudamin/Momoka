using Momoka.Emby;
using Momoka.MoviePilot;

namespace Momoka.AniBt;

/// <summary>菜单资格只读媒体类型，不把父剧的类型写回单集元数据。</summary>
public static class AniBtEligibility
{
    public static bool Supports(EmbyItem item) =>
        item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    public static bool HasGenre(EmbyItem item) => item.Genres.Any(genre =>
        genre.Contains("动画", StringComparison.Ordinal) || genre.Contains("动漫", StringComparison.Ordinal));

    /// <summary>
    /// 订阅详情那颗「在 AniBT 中搜索」的显示条件：类型是动画，且能从出品国家、原始语言或 Bangumi
    /// 来源确认它是日本动画。缺国家与语言时宁可不显示，也不把国漫之类的订阅交给 AniBT。
    /// </summary>
    public static bool IsJapaneseAnime(MoviePilotMediaDetail? detail)
    {
        if (detail is null || !IsAnimeGenre(detail.Genres)) return false;
        return detail.Countries.Contains("日本", StringComparison.Ordinal)
            || detail.Countries.Contains("Japan", StringComparison.OrdinalIgnoreCase)
            || detail.OriginalLanguage.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || detail.OriginalLanguage.Equals("jpn", StringComparison.OrdinalIgnoreCase)
            || detail.Media.MediaSource?.Equals("bangumi", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsAnimeGenre(string genres) =>
        genres.Contains("动画", StringComparison.Ordinal)
        || genres.Contains("动漫", StringComparison.Ordinal)
        || genres.Contains("番剧", StringComparison.Ordinal)
        || genres.Contains("アニメ", StringComparison.Ordinal)
        || genres.Contains("Animation", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> ResolveAsync(EmbyClient client, EmbyItem item, CancellationToken cancellationToken)
    {
        if (!Supports(item)) return false;
        if (HasGenre(item)) return true;
        var detail = await client.GetItemAsync(item.Id, cancellationToken, "Genres,SeriesId").ConfigureAwait(false);
        if (HasGenre(detail)) return true;
        if (detail.Type is not (EmbyItemType.Episode or EmbyItemType.Season)) return false;
        var seriesId = detail.SeriesId ?? item.SeriesId;
        if (string.IsNullOrWhiteSpace(seriesId)) return false;
        var series = await client.GetItemAsync(seriesId, cancellationToken, "Genres").ConfigureAwait(false);
        return series.Type == EmbyItemType.Series && HasGenre(series);
    }
}
