namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>The native playlist picker only lists playlists this account can edit.</summary>
    public async Task<IReadOnlyList<EmbyItem>> GetEditablePlaylistsAsync(CancellationToken cancellationToken)
    {
        var items = await GetServerPlaylistsAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(item => item.CanEditItems == true).ToArray();
    }

    /// <summary>The video-library picker must not offer an audio/unknown playlist as a writable target.</summary>
    public async Task<IReadOnlyList<EmbyItem>> GetEditableVideoPlaylistsAsync(CancellationToken cancellationToken)
    {
        var items = await GetServerPlaylistsAsync(cancellationToken).ConfigureAwait(false);
        return items.Where(item => item.CanEditItems == true && item.MediaType == "Video").ToArray();
    }

    /// <summary>Creates the playlist with its complete initial selection in one request.</summary>
    public async Task<EmbyPlaylistCreationResult> CreatePlaylistAsync(string name, IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken, Action? confirmed = null, Action? beforeWrite = null)
    {
        RequireServerPlaylists();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请填写播放列表名称", nameof(name));
        var ids = PlaylistIds(itemIds, allowEmpty: true);
        // An empty Jellyfin playlist otherwise defaults to Audio in PlaylistManager.
        var url = Connection.IsJellyfin ? Url("Playlists") : Url("Playlists", ("Name", name.Trim()),
            ("Ids", ids.Length > 0 ? ids : null), ("UserId", Connection.UserId), ("MediaType", "Video"));
        object? body = Connection.IsJellyfin ? new
        {
            Name = name.Trim(),
            Ids = itemIds,
            UserId = Connection.UserId,
            MediaType = "Video",
            Users = Array.Empty<object>(),
            IsPublic = false
        } : null;
        cancellationToken.ThrowIfCancellationRequested();
        beforeWrite?.Invoke();
        return await http.WritePlaylistForJsonAsync<EmbyPlaylistCreationResult>(HttpMethod.Post, url,
            body, Context, cancellationToken, confirmed)
            .ConfigureAwait(false) ?? new();
    }

    public Task AddToPlaylistAsync(string playlistId, IReadOnlyList<string> itemIds, bool skipDuplicates,
        CancellationToken cancellationToken, Action? confirmed = null, Action? beforeWrite = null)
    {
        RequireServerPlaylists();
        var id = PlaylistSegment(playlistId);
        var ids = PlaylistIds(itemIds);
        return AddToPlaylistCoreAsync(playlistId, id, itemIds, ids, skipDuplicates, cancellationToken, confirmed, beforeWrite);
    }

    private async Task AddToPlaylistCoreAsync(string playlistId, string id, IReadOnlyList<string> itemIds,
        string ids, bool skipDuplicates, CancellationToken cancellationToken, Action? confirmed, Action? beforeWrite)
    {
        await RequirePlaylistEditAsync(playlistId, cancellationToken).ConfigureAwait(false);
        if (Connection.IsJellyfin)
        {
            if (skipDuplicates)
            {
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var members = await ReadAllPlaylistMembersAsync(playlistId, cancellationToken).ConfigureAwait(false);
                foreach (var item in members) existing.Add(item.Id);
                var selected = itemIds.Where(existing.Add).ToArray();
                if (selected.Length == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    beforeWrite?.Invoke();
                    return;
                }
                ids = PlaylistIds(selected);
                // A second read may have spanned a permission change; refresh the native grant.
                await RequirePlaylistEditAsync(playlistId, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeWrite?.Invoke();
            await http.WritePlaylistAsync(HttpMethod.Post, Url($"Playlists/{id}/Items",
                ("Ids", ids), ("UserId", Connection.UserId)), null, Context, cancellationToken, confirmed).ConfigureAwait(false);
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        beforeWrite?.Invoke();
        await http.WritePlaylistAsync(HttpMethod.Post, Url($"Playlists/{id}/Items"),
            new { Ids = ids, UserId = Connection.UserId, SkipDuplicates = skipDuplicates }, Context, cancellationToken, confirmed)
            .ConfigureAwait(false);
    }

    /// <summary>Scans the selected folder's files using the same Default refresh as Emby Web.</summary>
    public Task ScanLibraryFilesAsync(string itemId, CancellationToken cancellationToken)
    {
        RequireEmby("扫描媒体库文件");
        return http.PostAsync(Url($"Items/{CollectionSegment(itemId)}/Refresh", ("Recursive", "true"),
            ("ImageRefreshMode", "Default"), ("MetadataRefreshMode", "Default"),
            ("ReplaceAllImages", "false"), ("ReplaceAllMetadata", "false")), null, Context, cancellationToken);
    }
}
