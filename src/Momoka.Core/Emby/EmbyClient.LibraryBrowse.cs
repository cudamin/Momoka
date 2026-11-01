namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public Task<ItemsResult> GetLibraryItemsAsync(string collectionType, LibrarySection section, string? group,
        ItemQuery query, CancellationToken cancellationToken)
    {
        // Jellyfin's 我的最爱 tab is no longer one of its library tabs (its web client has no such tab on
        // a series library), but the per-library favorites query itself is still a valid route and is
        // used by the old favorites-page tests and any saved navigation. Keep it callable.
        var supported = LibraryBrowse.Sections(collectionType, Connection.ServerKind).Any(entry => entry.Section == section)
            || Connection.IsJellyfin && section == LibrarySection.Favorites;
        if (!supported) throw new NotSupportedException("当前服务器不支持此媒体库分类");
        if (Connection.IsFnNas || Connection.IsPlex) return GetItemsAsync(query, cancellationToken);
        var path = LibraryBrowse.Path(section, group, Connection.UserId);
        if (section == LibrarySection.Upcoming)
            return http.GetJsonAsync<ItemsResult>(Url(path, LibraryUpcoming.Parameters(query, Connection.UserId, Connection.ServerKind)),
                Context, cancellationToken);
        var parameters = query.ToParameters().ToList();
        if (path is "Genres" or "Studios" or "Trailers") parameters.Add(("UserId", Connection.UserId));
        return http.GetJsonAsync<ItemsResult>(Url(path, [.. parameters]), Context, cancellationToken);
    }

    /// <summary>
    /// 补齐库根条目的 <see cref="EmbyItem.Subviews"/>。实测 4.10.1.0：<c>/Views</c> 即使点名
    /// <c>Fields=Subviews</c> 也不带它，只有按条目问才发；混合内容库没有 CollectionType，没有这一步
    /// 就只剩「文件夹」一种样子。只问还没有类型判定的库，一个请求问齐。
    /// </summary>
    public async Task FetchLibrarySubviewsAsync(IReadOnlyList<EmbyItem> libraries, CancellationToken cancellationToken)
    {
        if (Connection.IsFnNas || Connection.IsPlex) return;
        var ids = libraries
            .Where(item => item.CollectionType is null && item.Subviews is null && item.Id.Length > 0)
            .Select(item => item.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0) return;
        var url = Url($"Users/{Connection.UserId}/Items",
            ("Ids", string.Join(',', ids)),
            // 没有这个类型筛选时，服务器不把 CollectionFolder 放进 Ids 查询的结果里。
            ("IncludeItemTypes", EmbyItemType.CollectionFolder),
            ("Fields", "Subviews"));
        var result = await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
        var found = result.Items
            .Where(item => item.Subviews is not null)
            .ToDictionary(item => item.Id, item => item.Subviews!, StringComparer.Ordinal);
        foreach (var library in libraries)
            if (library.Subviews is null && found.TryGetValue(library.Id, out var subviews))
                library.Subviews = subviews;
    }
}
