using System.Globalization;

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>The account's server-persisted directory, kept separate from this playback session's queue.</summary>
    public async Task<IReadOnlyList<EmbyItem>> GetServerPlaylistsAsync(CancellationToken cancellationToken)
    {
        RequireServerPlaylists();
        const int PageSize = 200;
        var items = new List<EmbyItem>();
        var start = 0;
        int? total = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await http.GetJsonAsync<ItemsResult>(Url($"Users/{PlaylistSegment(Connection.UserId)}/Items",
                ("Recursive", "true"), ("IncludeItemTypes", "Playlist"), ("SortBy", "SortName"),
                ("Fields", "PrimaryImageAspectRatio,ChildCount"), ("EnableUserData", "false"),
                ("StartIndex", start.ToString(CultureInfo.InvariantCulture)), ("Limit", PageSize.ToString(CultureInfo.InvariantCulture))),
                Context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (total is { } previousTotal && previousTotal != page.TotalRecordCount)
                throw new InvalidOperationException("服务器播放列表目录在读取中已变化，请重新加载。");
            total = page.TotalRecordCount;
            if (page.Items.Count == 0 && start < page.TotalRecordCount)
                throw new InvalidOperationException("服务器播放列表目录未完整返回，请重新加载。");
            foreach (var item in page.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || item.Type != EmbyItemType.Playlist) continue;
                if (!seen.Add(item.Id)) throw new InvalidOperationException("服务器返回了重复的播放列表目录页，请重新加载。");
                if (Connection.IsJellyfin)
                    item.CanEditItems = await GetPlaylistEditPermissionAsync(item.Id, cancellationToken).ConfigureAwait(false);
                items.Add(item);
            }
            start = checked(start + page.Items.Count);
            if (page.Items.Count == 0 || page.TotalRecordCount > 0 && start >= page.TotalRecordCount
                || page.TotalRecordCount == 0 && page.Items.Count < PageSize) return items;
        }
    }

    /// <summary>Jellyfin's own CanEdit is independent of administrator and collection permissions.</summary>
    public async Task<bool> GetPlaylistEditPermissionAsync(string playlistId, CancellationToken cancellationToken)
    {
        RequireServerPlaylists();
        var id = PlaylistSegment(playlistId);
        try
        {
            var item = await http.GetJsonAsync<EmbyItem>(Url($"Users/{PlaylistSegment(Connection.UserId)}/Items/{id}",
                ("Fields", "PrimaryImageAspectRatio")), Context, cancellationToken).ConfigureAwait(false);
            if (item.Type != EmbyItemType.Playlist || item.Id != playlistId || item.MediaType != "Video") return false;
            if (Connection.IsJellyfin)
            {
                var permission = await http.GetJsonAsync<PlaylistUserPermission>(
                    Url($"Playlists/{id}/Users/{PlaylistSegment(Connection.UserId)}"), Context, cancellationToken).ConfigureAwait(false);
                return permission.CanEdit == true && SamePlaylistUser(permission.UserId, Connection.UserId);
            }
            return item.CanEditItems == true;
        }
        catch (EmbyApiException error) when (error.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Remove only these server membership identities, including one of several duplicate media entries.</summary>
    public async Task RemoveFromPlaylistAsync(string playlistId, IReadOnlyList<string> entryIds,
        CancellationToken cancellationToken, Action? confirmed = null, Action? beforeWrite = null)
    {
        RequireServerPlaylists();
        var id = PlaylistSegment(playlistId);
        var entries = PlaylistIds(entryIds);
        await RequirePlaylistEditAsync(playlistId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        beforeWrite?.Invoke();
        var path = $"Playlists/{id}/Items";
        await http.WritePlaylistAsync(Connection.IsJellyfin ? HttpMethod.Delete : HttpMethod.Post,
            Url(Connection.IsJellyfin ? path : path + "/Delete", ("EntryIds", entries)), null,
            Context, cancellationToken, confirmed).ConfigureAwait(false);
    }

    /// <summary>newIndex is zero-based in the server's original visible member order.</summary>
    public async Task MovePlaylistItemAsync(string playlistId, string entryId, int newIndex,
        CancellationToken cancellationToken, Action? confirmed = null, Action? beforeWrite = null)
    {
        RequireServerPlaylists();
        var id = PlaylistSegment(playlistId);
        var entry = PlaylistSegment(entryId);
        if (newIndex < 0) throw new ArgumentOutOfRangeException(nameof(newIndex));
        await RequirePlaylistEditAsync(playlistId, cancellationToken).ConfigureAwait(false);
        var members = await ReadAllPlaylistMembersAsync(playlistId, cancellationToken).ConfigureAwait(false);
        if (!members.Any(item => string.Equals(item.PlaylistItemId, entryId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("当前播放列表已没有这个成员，请刷新列表。");
        if (newIndex >= members.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        await RequirePlaylistEditAsync(playlistId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        beforeWrite?.Invoke();
        await http.WritePlaylistAsync(HttpMethod.Post,
            Url($"Playlists/{id}/Items/{entry}/Move/{newIndex.ToString(CultureInfo.InvariantCulture)}"), null,
            Context, cancellationToken, confirmed).ConfigureAwait(false);
    }

    private async Task RequirePlaylistEditAsync(string playlistId, CancellationToken cancellationToken)
    {
        if (!await GetPlaylistEditPermissionAsync(playlistId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("当前账号没有此播放列表的编辑权限。");
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<IReadOnlyList<EmbyItem>> ReadAllPlaylistMembersAsync(string playlistId, CancellationToken cancellationToken)
    {
        const int PageSize = 200;
        var members = new List<EmbyItem>();
        var pages = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;
        int? total = null;
        while (true)
        {
            var page = await GetPlaylistItemsAsync(playlistId, start, PageSize, cancellationToken).ConfigureAwait(false);
            if (total is { } previousTotal && previousTotal != page.TotalRecordCount)
                throw new InvalidOperationException("播放列表成员在读取中已变化，请重新加载。");
            total = page.TotalRecordCount;
            if (page.Items.Count == 0)
            {
                if (start < page.TotalRecordCount) throw new InvalidOperationException("播放列表成员未完整返回，请重新加载。");
                return members;
            }
            var stamp = string.Join('\n', page.Items.Select(item => item.Id + ":" + item.PlaylistItemId));
            if (!pages.Add(stamp)) throw new InvalidOperationException("服务器返回了重复的播放列表成员页，请重新加载。");
            members.AddRange(page.Items);
            start = checked(start + page.Items.Count);
            if (page.TotalRecordCount > 0 && start >= page.TotalRecordCount
                || page.TotalRecordCount == 0 && page.Items.Count < PageSize) return members;
        }
    }

    private void RequireServerPlaylists()
    {
        if (Connection.ServerKind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin))
            throw new NotSupportedException("当前服务器不支持 Emby/Jellyfin 播放列表管理。");
    }

    private static bool SamePlaylistUser(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal)
        || Guid.TryParse(actual, out var actualGuid) && Guid.TryParse(expected, out var expectedGuid) && actualGuid == expectedGuid;

    private static string PlaylistSegment(string value) => Uri.EscapeDataString(PlaylistId(value));

    private static string PlaylistId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Any(char.IsControl)
            || value.IndexOfAny(['/', '\\', '?', '#', ',']) >= 0)
            throw new ArgumentException("播放列表或成员标识无效。", nameof(value));
        return value;
    }

    private static string PlaylistIds(IReadOnlyList<string> values, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!allowEmpty && values.Count == 0) throw new ArgumentException("请选择播放列表成员。", nameof(values));
        return string.Join(',', values.Select(PlaylistId));
    }

    private sealed class PlaylistUserPermission
    {
        public string? UserId { get; set; }
        public bool? CanEdit { get; set; }
    }
}
