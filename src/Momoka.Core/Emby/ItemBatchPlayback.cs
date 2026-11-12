namespace Momoka.Emby;

/// <summary>Collects one captured account's selected items without starting playback.</summary>
public static class ItemBatchPlayback
{
    private const int PageSize = 100;

    public static bool CanCollect(EmbyItem item, MediaServerKind kind = MediaServerKind.Emby) =>
        kind is MediaServerKind.Emby or MediaServerKind.Jellyfin or MediaServerKind.Plex
        && !item.IsDisplayOnly && !string.IsNullOrWhiteSpace(item.Id)
        && (item.IsPlayable || kind is MediaServerKind.Emby or MediaServerKind.Jellyfin
            && item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Folder
                or EmbyItemType.BoxSet or EmbyItemType.Playlist or EmbyItemType.CollectionFolder);

    /// <summary>
    /// Retains selection order and the first occurrence of each media identity. A failed read never
    /// returns a partial queue; cancellation or revoked query/account ownership returns an empty list.
    /// </summary>
    public static async Task<IReadOnlyList<EmbyItem>> CollectAsync(EmbySessionScope source,
        IReadOnlyList<EmbyItem> selected, Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(isCurrent);
        if (!Current()) return [];
        var selection = selected.ToArray();
        if (selection.Any(item => !SameOwner(item) || !CanCollect(item, source.Connection.ServerKind)))
            throw new InvalidOperationException("所选媒体或账号不允许批量播放");
        var identities = selection.Select(item => item.IdentityKey).ToArray();
        var collected = new List<EmbyItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in selection)
            {
                if (!SelectionCurrent()) return [];
                if (item.IsPlayable)
                {
                    Add(item);
                    continue;
                }

                var children = await ReadChildrenAsync(item).ConfigureAwait(true);
                if (!SelectionCurrent()) return [];
                var playable = children.Where(child => child.IsPlayable && !child.IsDisplayOnly).ToArray();
                if (playable.Length == 0)
                    throw new InvalidOperationException($"“{item.Name}”没有可播放的内容");
                foreach (var child in item.Type == EmbyItemType.Series ? ItemDetail.AirOrder(playable) : playable) Add(child);
            }
        }
        catch (OperationCanceledException) { return []; }
        return SelectionCurrent() ? collected.ToArray() : [];

        bool Current() => !cancellationToken.IsCancellationRequested && source.IsAvailable && isCurrent();
        bool SameOwner(EmbyItem item) => item.Source is { IsAvailable: true } owner
            && ReferenceEquals(owner.UserStateOwner, source.UserStateOwner);
        bool SelectionCurrent() => Current() && selection.Select((item, index) => SameOwner(item)
            && item.IdentityKey == identities[index]).All(current => current);
        void Add(EmbyItem item)
        {
            if (!SameOwner(item) || string.IsNullOrWhiteSpace(item.Id))
                throw new InvalidOperationException("读取的媒体与所选账号不一致");
            if (seen.Add(item.IdentityKey)) collected.Add(item);
        }
        async Task<List<EmbyItem>> ReadChildrenAsync(EmbyItem parent)
        {
            var children = new List<EmbyItem>();
            var pages = new HashSet<string>(StringComparer.Ordinal);
            var start = 0;
            int? total = null;
            while (SelectionCurrent())
            {
                var page = await source.ExecuteAsync((client, token) => parent.Type == EmbyItemType.Playlist
                    ? client.GetPlaylistItemsAsync(parent.Id, start, PageSize, token)
                    : client.GetItemsAsync(new ItemQuery
                    {
                        ParentId = parent.Id,
                        Recursive = true,
                        IncludeItemTypes = parent.Type is EmbyItemType.Series or EmbyItemType.Season
                            ? [EmbyItemType.Episode]
                            : [EmbyItemType.Movie, EmbyItemType.Episode, EmbyItemType.Video, EmbyItemType.MusicVideo, EmbyItemType.Trailer],
                        SortBy = parent.Type is EmbyItemType.Series or EmbyItemType.Season ? EmbySortBy.Number : EmbySortBy.Name,
                        StartIndex = start,
                        Limit = PageSize
                    }, token), cancellationToken).ConfigureAwait(true);
                if (!SelectionCurrent()) return [];
                if (total is { } previousTotal && previousTotal != page.TotalRecordCount)
                    throw new InvalidOperationException($"“{parent.Name}”的内容在收集中已变化，请重试");
                total = page.TotalRecordCount;
                if (page.Items.Count == 0)
                {
                    if (start < page.TotalRecordCount)
                        throw new InvalidOperationException($"“{parent.Name}”的播放清单未完整返回，请重试");
                    break;
                }
                var stamp = string.Join('\n', page.Items.Select(child => child.IdentityKey));
                if (!pages.Add(stamp)) throw new InvalidOperationException($"“{parent.Name}”返回了重复的播放清单页，请重试");
                children.AddRange(page.Items);
                start = checked(start + page.Items.Count);
                if (start >= page.TotalRecordCount) break;
            }
            return children;
        }
    }
}
