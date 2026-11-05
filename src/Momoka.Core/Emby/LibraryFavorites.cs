using Momoka.Infrastructure;

namespace Momoka.Emby;

public enum FavoriteShelf
{
    Shows, Episodes
}

public sealed record LibraryFavoriteRow(FavoriteShelf Shelf, string Title, bool Wide);

/// <summary>电视库收藏按节目与单集分别查询，不从收藏节目推导其子集的收藏状态。</summary>
public static class LibraryFavorites
{
    public const int PageSize = 100;
    public const int PosterWidth = 148;
    public const int EpisodeWidth = 216;
    public const int CardChrome = 40;

    public static int WidthFor(bool wide, double viewportWidth) =>
        (int)Math.Clamp(Math.Floor(double.IsFinite(viewportWidth) ? viewportWidth : wide ? EpisodeWidth : PosterWidth),
            1, wide ? EpisodeWidth : PosterWidth);

    public static double RowHeight(int width, bool wide) => CardSize.HeightFor(width, wide) + CardChrome;

    public static IReadOnlyList<LibraryFavoriteRow> Rows(string? collectionType, MediaServerKind serverKind) =>
        collectionType == "tvshows" && serverKind is MediaServerKind.Emby or MediaServerKind.Jellyfin
            ? [new(FavoriteShelf.Shows, "收藏节目", Wide: false), new(FavoriteShelf.Episodes, "收藏集", Wide: true)]
            : [];

    public static ItemQuery Query(string parentId, FavoriteShelf shelf, int start = 0, int limit = PageSize) => new()
    {
        ParentId = parentId,
        Recursive = true,
        IncludeItemTypes = shelf switch
        {
            FavoriteShelf.Shows => [EmbyItemType.Series],
            FavoriteShelf.Episodes => [EmbyItemType.Episode],
            _ => throw new ArgumentOutOfRangeException(nameof(shelf))
        },
        Fields = EmbyFields.Browse,
        IsFavorite = true,
        SortBy = EmbySortBy.Name,
        StartIndex = start,
        Limit = limit
    };

    public static string Title(EmbyItem item) =>
        item.Type == EmbyItemType.Episode ? LibraryUpcoming.Title(item) : item.Name;

    public static string? Subtitle(EmbyItem item) => item.Type switch
    {
        EmbyItemType.Episode => LibraryUpcoming.Subtitle(item),
        EmbyItemType.Series => LibraryRecommended.Subtitle(item),
        _ => null
    };
}
