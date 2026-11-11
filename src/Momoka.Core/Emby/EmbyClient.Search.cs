namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>按来源提供搜索类别；Jellyfin 人物不属于递归的媒体目录。</summary>
    public Task<ItemsResult> SearchItemsAsync(string? term, LibrarySearchCategory category, int start, int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(term)) return Task.FromResult(new ItemsResult());
        var query = LibrarySearch.Query(term, category, start, limit, Connection.ServerKind)!;
        return category == LibrarySearchCategory.People
            ? QuerySearchPeopleAsync(term, start, limit, cancellationToken)
            : GetItemsAsync(query, cancellationToken);
    }

    /// <summary>
    /// 旧 Jellyfin 的 /Persons 只接受 limit，不接受 startIndex，总数也是取回的人数。
    /// limit=0 在服务端不加 SQL LIMIT；只取匹配人物，再在本地分页，保留服务端顺序。
    /// </summary>
    public async Task<ItemsResult> QuerySearchPeopleAsync(string? term, int start, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(term)) return new ItemsResult();
        var query = LibrarySearch.Query(term, LibrarySearchCategory.People, start, limit, Connection.ServerKind)!;
        if (!Connection.IsJellyfin) return await GetItemsAsync(query, cancellationToken).ConfigureAwait(false);
        var result = await http.GetJsonAsync<ItemsResult>(Url("Persons", ("UserId", Connection.UserId),
            ("SearchTerm", term.Trim()), ("Limit", "0"), ("Fields", EmbyFields.Browse), ("ImageTypeLimit", "1")),
            Context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ItemsResult
        {
            Items = result.Items.Skip(start).Take(limit).ToList(),
            TotalRecordCount = result.Items.Count
        };
    }

    /// <summary>原有成员顺序是播放列表的身份；不借标题排序或递归文件夹查询。</summary>
    public Task<ItemsResult> GetPlaylistItemsAsync(string playlistId, int start, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Connection.ServerKind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin))
            throw new NotSupportedException("当前服务器不支持此播放列表成员读取");
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        if (playlistId is "." or ".." || playlistId.Any(char.IsControl) || playlistId.IndexOfAny(['/', '\\', '?', '#', ',']) >= 0)
            throw new ArgumentException("播放列表标识无效", nameof(playlistId));
        if (Connection.IsJellyfin)
            return http.GetJsonAsync<ItemsResult>(Url($"Playlists/{Uri.EscapeDataString(playlistId)}/Items",
                ("UserId", Connection.UserId), ("StartIndex", start.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Fields", EmbyFields.Browse)),
                Context, cancellationToken);
        return GetItemsAsync(new ItemQuery
        {
            ParentId = playlistId,
            SortBy = "ListItemOrder",
            Descending = false,
            StartIndex = start,
            Limit = limit
        }, cancellationToken);
    }
}
