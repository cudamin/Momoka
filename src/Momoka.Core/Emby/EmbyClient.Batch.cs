namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>The native playlist picker only lists playlists this account can edit.</summary>
    public async Task<IReadOnlyList<EmbyItem>> GetEditablePlaylistsAsync(CancellationToken cancellationToken)
    {
        RequireEmby("播放列表管理");
        var items = new List<EmbyItem>();
        const int PageSize = 200;
        for (var start = 0; ; start += PageSize)
        {
            var result = await http.GetJsonAsync<ItemsResult>(Url($"Users/{CollectionSegment(Connection.UserId)}/Items",
                ("Recursive", "true"), ("IncludeItemTypes", "Playlist"), ("SortBy", "SortName"),
                ("Fields", "PrimaryImageAspectRatio"), ("EnableUserData", "false"), ("CanEditItems", "true"),
                ("StartIndex", start.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Limit", "200")),
                Context, cancellationToken).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (result.Items.Count < PageSize || result.TotalRecordCount > 0 && start + result.Items.Count >= result.TotalRecordCount)
                return items;
        }
    }

    /// <summary>Creates the playlist with its complete initial selection in one request.</summary>
    public async Task<EmbyPlaylistCreationResult> CreatePlaylistAsync(string name, IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken)
    {
        RequireEmby("播放列表管理");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请填写播放列表名称", nameof(name));
        // Optional receipt metadata must not make an accepted create look like a retryable write.
        return await http.PostForJsonAsync<EmbyPlaylistCreationResult>(Url("Playlists", ("Name", name.Trim()),
            ("Ids", BatchIds(itemIds)), ("UserId", Connection.UserId)), null, Context, cancellationToken)
            .ConfigureAwait(false) ?? new();
    }

    public Task AddToPlaylistAsync(string playlistId, IReadOnlyList<string> itemIds, bool skipDuplicates,
        CancellationToken cancellationToken)
    {
        RequireEmby("播放列表管理");
        var ids = BatchIds(itemIds);
        return http.PostAsync(Url($"Playlists/{CollectionSegment(playlistId)}/Items"),
            new { Ids = ids, UserId = Connection.UserId, SkipDuplicates = skipDuplicates }, Context, cancellationToken);
    }

    /// <summary>Conversion uses only server conversion providers, never a download target.</summary>
    public Task<EmbyConversionOptions> GetConversionOptionsAsync(IReadOnlyList<string> itemIds, string? targetId,
        CancellationToken cancellationToken)
    {
        RequireEmby("媒体转换");
        return http.GetJsonAsync<EmbyConversionOptions>(Url("Sync/Options", ("UserId", Connection.UserId),
            ("ItemIds", BatchIds(itemIds)), ("IncludeProviders", "ConvertSyncProvider"), ("TargetId", targetId)),
            Context, cancellationToken);
    }

    /// <summary>ItemLimit and the automatic-conversion switches belong to this single shared job.</summary>
    public async Task<EmbyConversionJobReceipt> CreateConversionJobAsync(IReadOnlyList<string> itemIds,
        EmbyConversionSelection selection, CancellationToken cancellationToken)
    {
        RequireEmby("媒体转换");
        if (string.IsNullOrWhiteSpace(selection.TargetId)) throw new ArgumentException("请选择转换目标", nameof(selection));
        if (selection.Bitrate is <= 0 || selection.ItemLimit is <= 0)
            throw new ArgumentException("转换码率和数量上限必须大于零", nameof(selection));
        return await http.PostForJsonAsync<EmbyConversionJobReceipt>(Url("Sync/Jobs"), new
        {
            UserId = Connection.UserId, ItemIds = BatchIds(itemIds), selection.TargetId,
            selection.Profile, selection.Quality, selection.Bitrate, selection.Container,
            selection.VideoCodec, selection.AudioCodec, selection.UnwatchedOnly, selection.SyncNewContent, selection.ItemLimit
        }, Context, cancellationToken).ConfigureAwait(false) ?? new();
    }

    /// <summary>Scans the selected folder's files using the same Default refresh as Emby Web.</summary>
    public Task ScanLibraryFilesAsync(string itemId, CancellationToken cancellationToken)
    {
        RequireEmby("扫描媒体库文件");
        return http.PostAsync(Url($"Items/{CollectionSegment(itemId)}/Refresh", ("Recursive", "true"),
            ("ImageRefreshMode", "Default"), ("MetadataRefreshMode", "Default"),
            ("ReplaceAllImages", "false"), ("ReplaceAllMetadata", "false")), null, Context, cancellationToken);
    }

    private static string BatchIds(IReadOnlyList<string> itemIds)
    {
        if (itemIds.Count == 0) throw new ArgumentException("请选择媒体条目", nameof(itemIds));
        return string.Join(',', itemIds.Select(CollectionId));
    }
}
