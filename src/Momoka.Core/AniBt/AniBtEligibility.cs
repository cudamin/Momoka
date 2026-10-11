using Momoka.Emby;

namespace Momoka.AniBt;

/// <summary>菜单资格只读媒体类型，不把父剧的类型写回单集元数据。</summary>
public static class AniBtEligibility
{
    public static bool Supports(EmbyItem item) =>
        item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    public static bool HasGenre(EmbyItem item) => item.Genres.Any(genre =>
        genre.Contains("动画", StringComparison.Ordinal) || genre.Contains("动漫", StringComparison.Ordinal));

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
