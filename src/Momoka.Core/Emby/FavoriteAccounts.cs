namespace Momoka.Emby;

/// <summary>标题栏收藏页的来源范围：聚合所有能列收藏的在线账号，并可只留其中一个。</summary>
public static class FavoriteAccounts
{
    public static IReadOnlyList<EmbySessionScope> Applicable(IEnumerable<EmbySessionScope> sources) =>
        [.. sources.Where(source => source.Connection.SupportsFavorites)];

    /// <summary>null 表示全部；调用方先确认 key 属于 <paramref name="sources"/>，未知键不给结果。</summary>
    public static IReadOnlyList<EmbySessionScope> Select(IReadOnlyList<EmbySessionScope> sources, string? key) =>
        key is null ? sources : [.. sources.Where(source => source.Key == key)];
}
