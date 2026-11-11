namespace Momoka.Emby;

public sealed record ItemBatchEntry(EmbyItem Item, CollectionMembership? Membership = null);
public enum ItemBatchStatus { Succeeded, Failed, Canceled }

public sealed record ItemBatchOptions(string? PlaylistId = null, bool SkipPlaylistDuplicates = true,
    string RefreshMode = "missing", bool ReplaceImages = false, bool ReplaceThumbnails = false,
    EmbyConversionSelection? Conversion = null, string? NewPlaylistName = null);

/// <summary>A confirmed write and its optional read-back have separate outcomes.</summary>
public sealed record ItemBatchItemResult(ItemBatchEntry Entry, ItemBatchStatus Status,
    EmbyUserData? UserData = null, EmbyUserData? RefreshedUserData = null,
    string? Error = null, string? RefreshError = null, string? ConversionJobId = null, string? PlaylistId = null)
{
    public EmbyItem Item => Entry.Item;
}

public sealed record ItemBatchResult(IReadOnlyList<ItemBatchItemResult> Results)
{
    public int Succeeded => Results.Count(result => result.Status == ItemBatchStatus.Succeeded);
    public int Failed => Results.Count(result => result.Status == ItemBatchStatus.Failed);
    public int Canceled => Results.Count(result => result.Status == ItemBatchStatus.Canceled);
}

public static class ItemBatch
{
    public static IReadOnlyList<ItemCommand> Commands { get; } = Array.AsReadOnly(new[]
    {
        ItemCommand.MarkPlayed, ItemCommand.MarkUnplayed, ItemCommand.Favorite, ItemCommand.Unfavorite,
        ItemCommand.AddToCollection, ItemCommand.RemoveFromCollection,
        ItemCommand.Play, ItemCommand.PlayFromBeginning, ItemCommand.Shuffle,
        ItemCommand.AddToPlaylist, ItemCommand.Convert, ItemCommand.Delete, ItemCommand.ResetMetadata,
        ItemCommand.RefreshMetadata, ItemCommand.ScanLibraryFiles
    });

    public static bool Allowed(ItemCommand command, EmbySessionScope source, ItemBatchEntry entry, ItemMenuPermissions permissions)
    {
        var item = entry.Item;
        if (!source.IsAvailable || item.Source is not { IsAvailable: true } owner
            || !ReferenceEquals(owner.UserStateOwner, source.UserStateOwner)
            || string.IsNullOrWhiteSpace(item.Id) || item.IsDisplayOnly) return false;
        var kind = source.Connection.ServerKind;
        if (kind == MediaServerKind.Plex)
            return command is ItemCommand.MarkPlayed or ItemCommand.MarkUnplayed
                && item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;
        if (kind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin)) return false;
        return command switch
        {
            ItemCommand.MarkPlayed or ItemCommand.MarkUnplayed or ItemCommand.Favorite or ItemCommand.Unfavorite => item.TracksUserState,
            ItemCommand.AddToCollection => item.Type is not (EmbyItemType.CollectionFolder or EmbyItemType.Person)
                && ItemCollections.CanManage(kind, permissions, item),
            ItemCommand.RemoveFromCollection => entry.Membership?.Matches(item) == true
                && ItemCollections.CanManage(kind, permissions, item),
            ItemCommand.Play or ItemCommand.PlayFromBeginning or ItemCommand.Shuffle => kind == MediaServerKind.Emby && CanCollect(item),
            ItemCommand.AddToPlaylist => kind == MediaServerKind.Emby && CanAddToPlaylist(item),
            ItemCommand.Convert => kind == MediaServerKind.Emby && CanConvert(item) && permissions.CanConvertMedia != false,
            ItemCommand.Delete => kind == MediaServerKind.Emby && CanDelete(item, permissions),
            ItemCommand.ResetMetadata => kind == MediaServerKind.Emby && ItemIdentify.Supports(item)
                && item.Type != EmbyItemType.Person && permissions.IsAdministrator != false,
            ItemCommand.RefreshMetadata => kind == MediaServerKind.Emby && CanRefresh(item)
                && permissions.IsAdministrator != false,
            ItemCommand.ScanLibraryFiles => kind == MediaServerKind.Emby && CanScan(item)
                && permissions.IsAdministrator != false,
            _ => false
        };
    }

    // Kept identical to ItemBatchPlayback.CanCollect until its independent implementation is integrated.
    private static bool CanCollect(EmbyItem item) => item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season
        or EmbyItemType.Folder or EmbyItemType.BoxSet or EmbyItemType.Playlist or EmbyItemType.CollectionFolder;

    private static bool CanAddToPlaylist(EmbyItem item) => item.CollectionType is not ("livetv" or "playlists")
        && item.MediaType != "Game" && (item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season
            or EmbyItemType.Folder or EmbyItemType.BoxSet or EmbyItemType.Playlist or EmbyItemType.CollectionFolder);

    private static bool CanConvert(EmbyItem item) => item.CollectionType is not ("livetv" or "playlists" or "boxsets")
        && item.MediaType is not ("Book" or "Photo" or "Game" or "Audio")
        && (item.LocationType != "Virtual" || item.IsFolder)
        && (item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Folder
            or EmbyItemType.BoxSet or EmbyItemType.CollectionFolder);

    private static bool CanDelete(EmbyItem item, ItemMenuPermissions permissions) => permissions.CanDeleteContent != false
        && item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode
            or EmbyItemType.Video or EmbyItemType.MusicVideo or EmbyItemType.Trailer or EmbyItemType.BoxSet or EmbyItemType.Playlist
        && item.CanDelete != false
        && (item.CanDelete != true || item.PermissionReadRevision == item.Source?.PermissionRevision);

    private static bool CanRefresh(EmbyItem item) => item.CollectionType is not ("livetv" or "boxsets")
        && (item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Folder
            or EmbyItemType.BoxSet or EmbyItemType.Playlist or EmbyItemType.CollectionFolder);

    private static bool CanScan(EmbyItem item) => item.IsFolder && CanRefresh(item)
        && item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Folder or EmbyItemType.CollectionFolder;
}

/// <summary>Runs one captured account's independent writes. Cancel means stop before the next item.</summary>
public sealed class ItemBatchService
{
    public async Task<ItemBatchResult> ExecuteAsync(EmbySessionScope source, IReadOnlyList<ItemBatchEntry> entries,
        ItemCommand command, Func<ItemMenuPermissions> permissions, string? collectionId,
        CancellationToken cancellationToken, Action<ItemBatchItemResult>? completed = null,
        Action<ItemBatchItemResult>? refreshed = null, ItemBatchOptions? options = null)
    {
        if (command == ItemCommand.Convert || command == ItemCommand.AddToPlaylist && options?.NewPlaylistName is not null)
            return await ExecuteAggregateAsync(source, entries, command, permissions, cancellationToken, completed, options)
                .ConfigureAwait(true);
        var results = new List<ItemBatchItemResult>(entries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ItemBatchItemResult result;
            if (cancellationToken.IsCancellationRequested || !source.IsAvailable)
                result = new(entry, ItemBatchStatus.Canceled);
            else if (!seen.Add(entry.Item.IdentityKey))
                result = new(entry, ItemBatchStatus.Failed, Error: "重复的媒体条目");
            else if (!ItemBatch.Allowed(command, source, entry, permissions()))
                result = new(entry, ItemBatchStatus.Failed, Error: "当前媒体或账号不允许此操作");
            else
                result = await WriteAsync(source, entry, command, permissions, collectionId, options).ConfigureAwait(true);
            // UI publication is downstream of the write receipt; its failure must never replay HTTP.
            try { completed?.Invoke(result); }
            catch (Exception error)
            {
                result = result with { RefreshError = result.RefreshError ?? error.Message };
            }
            if (result.Status == ItemBatchStatus.Succeeded && source.Connection.IsPlex
                && command is ItemCommand.MarkPlayed or ItemCommand.MarkUnplayed)
            {
                try
                {
                    var data = await source.ExecuteAsync((client, token) => client.GetPlexUserDataAsync(entry.Item.Id, token),
                        CancellationToken.None).ConfigureAwait(true);
                    result = result with { RefreshedUserData = data };
                    refreshed?.Invoke(result);
                }
                catch (Exception error) { result = result with { RefreshError = error.Message }; }
            }
            results.Add(result);
        }
        return new(results);
    }

    private static async Task<ItemBatchResult> ExecuteAggregateAsync(EmbySessionScope source, IReadOnlyList<ItemBatchEntry> entries,
        ItemCommand command, Func<ItemMenuPermissions> permissions, CancellationToken cancellationToken,
        Action<ItemBatchItemResult>? completed, ItemBatchOptions? options)
    {
        var results = new ItemBatchItemResult?[entries.Count];
        var eligible = new List<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            ItemBatchItemResult? rejected = null;
            if (cancellationToken.IsCancellationRequested || !source.IsAvailable)
                rejected = new(entry, ItemBatchStatus.Canceled);
            else if (!seen.Add(entry.Item.IdentityKey))
                rejected = new(entry, ItemBatchStatus.Failed, Error: "重复的媒体条目");
            else if (!ItemBatch.Allowed(command, source, entry, permissions()))
                rejected = new(entry, ItemBatchStatus.Failed, Error: "当前媒体或账号不允许此操作");
            if (rejected is null) eligible.Add(index);
            else results[index] = rejected;
        }
        var confirmed = false;
        string? conversionJobId = null;
        string? playlistId = null;
        try
        {
            if (eligible.Count > 0)
            {
                await source.ExecuteAsync(async (client, token) =>
                {
                    // A read or authentication retry can replace the account's permission snapshot.
                    // Each attempt keeps only currently allowed items; no old selection lends a grant.
                    void Recheck()
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        source.ThrowIfUnavailable();
                        for (var candidate = eligible.Count - 1; candidate >= 0; candidate--)
                        {
                            var index = eligible[candidate];
                            if (ItemBatch.Allowed(command, source, entries[index], permissions())) continue;
                            results[index] = new(entries[index], ItemBatchStatus.Failed,
                                Error: "当前媒体或账号不允许此操作");
                            eligible.RemoveAt(candidate);
                        }
                    }
                    Recheck();
                    if (eligible.Count == 0) return;
                    var ids = eligible.Select(index => entries[index].Item.Id).ToArray();
                    if (command == ItemCommand.Convert)
                    {
                        var selection = options?.Conversion ?? throw new InvalidOperationException("请选择转换目标与选项");
                        var choices = await client.GetConversionOptionsAsync(ids, selection.TargetId, token).ConfigureAwait(false);
                        EmbyConversion.Validate(choices, selection);
                        Recheck();
                        if (eligible.Count == 0) return;
                        ids = eligible.Select(index => entries[index].Item.Id).ToArray();
                        var receipt = await client.CreateConversionJobAsync(ids, selection, token).ConfigureAwait(false);
                        conversionJobId = receipt.Job?.Id;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(options?.PlaylistId))
                            throw new InvalidOperationException("不能同时选择已有列表和新列表名称");
                        Recheck();
                        if (eligible.Count == 0) return;
                        ids = eligible.Select(index => entries[index].Item.Id).ToArray();
                        var receipt = await client.CreatePlaylistAsync(options?.NewPlaylistName ?? "", ids, token).ConfigureAwait(false);
                        playlistId = receipt.Id;
                    }
                    // A server acceptance must survive the session's later result-publication guard.
                    confirmed = true;
                }, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception error) when (!confirmed)
        {
            var status = error is OperationCanceledException ? ItemBatchStatus.Canceled : ItemBatchStatus.Failed;
            foreach (var index in eligible)
                results[index] = new(entries[index], status, Error: error.Message);
        }
        catch (Exception) when (confirmed) { }
        if (confirmed)
        {
            source.ConfirmUserStateChange();
            var refreshError = command == ItemCommand.Convert
                ? string.IsNullOrWhiteSpace(conversionJobId) ? "服务器已接受转换任务，但未返回任务编号，请查看服务器任务列表" : null
                : string.IsNullOrWhiteSpace(playlistId) ? "服务器已接受播放列表创建，但未返回列表编号，请刷新播放列表" : null;
            foreach (var index in eligible)
                results[index] = new(entries[index], ItemBatchStatus.Succeeded,
                    RefreshError: refreshError, ConversionJobId: conversionJobId, PlaylistId: playlistId);
        }
        // HTTP continuations may run on a worker; callbacks touch the native UI and belong to
        // the caller's captured context after the entire server write has produced its receipt.
        return new(results.Select(result => Publish(result!, completed)).ToArray());
    }

    private static ItemBatchItemResult Publish(ItemBatchItemResult result, Action<ItemBatchItemResult>? completed)
    {
        try { completed?.Invoke(result); }
        catch (Exception error) { result = result with { RefreshError = result.RefreshError ?? error.Message }; }
        return result;
    }

    private static async Task<ItemBatchItemResult> WriteAsync(EmbySessionScope source, ItemBatchEntry entry,
        ItemCommand command, Func<ItemMenuPermissions> permissions, string? collectionId, ItemBatchOptions? options)
    {
        EmbyUserData? data = null;
        var confirmed = false;
        try
        {
            data = await source.ExecuteAsync(async (client, token) =>
            {
                if (!ItemBatch.Allowed(command, source, entry, permissions()))
                    throw new UnauthorizedAccessException("当前媒体或账号不允许此操作");
                var item = entry.Item;
                switch (command)
                {
                    case ItemCommand.MarkPlayed: data = await client.MarkPlayedAsync(item.Id, token).ConfigureAwait(false); break;
                    case ItemCommand.MarkUnplayed: data = await client.MarkUnplayedAsync(item.Id, token).ConfigureAwait(false); break;
                    case ItemCommand.Favorite: data = await client.SetFavoriteAsync(item.Id, true, token).ConfigureAwait(false); break;
                    case ItemCommand.Unfavorite: data = await client.SetFavoriteAsync(item.Id, false, token).ConfigureAwait(false); break;
                    case ItemCommand.AddToCollection:
                        if (string.IsNullOrWhiteSpace(collectionId) || collectionId == item.Id)
                            throw new InvalidOperationException("请选择有效合集");
                        await client.AddToCollectionAsync(collectionId, [item.Id], token).ConfigureAwait(false);
                        break;
                    case ItemCommand.RemoveFromCollection:
                        await client.RemoveFromCollectionAsync(entry.Membership!.CollectionId, [item.Id], token).ConfigureAwait(false);
                        break;
                    case ItemCommand.AddToPlaylist:
                        if (string.IsNullOrWhiteSpace(options?.PlaylistId) || options.PlaylistId == item.Id)
                            throw new InvalidOperationException("请选择有效播放列表");
                        await client.AddToPlaylistAsync(options.PlaylistId, [item.Id], options.SkipPlaylistDuplicates, token).ConfigureAwait(false);
                        break;
                    case ItemCommand.Delete:
                        await client.DeleteItemAsync(item.Id, token).ConfigureAwait(false);
                        break;
                    case ItemCommand.ResetMetadata:
                        await client.ResetMetadataAsync(item.Id, token).ConfigureAwait(false);
                        break;
                    case ItemCommand.RefreshMetadata:
                        await client.RefreshItemAsync(item.Id, options?.RefreshMode ?? "missing", options?.ReplaceImages ?? false,
                            options?.ReplaceThumbnails ?? false, token).ConfigureAwait(false);
                        break;
                    case ItemCommand.ScanLibraryFiles:
                        await client.ScanLibraryFilesAsync(item.Id, token).ConfigureAwait(false);
                        break;
                    default: throw new NotSupportedException("不支持此批量操作");
                }
                // Session post-await lifetime guards may reject UI use after sign-out; the HTTP
                // receipt already exists and must remain a successful write in this batch ledger.
                confirmed = true;
                return data;
            }, CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException error) when (!confirmed) { return new(entry, ItemBatchStatus.Canceled, Error: error.Message); }
        catch (Exception error) when (!confirmed) { return new(entry, ItemBatchStatus.Failed, Error: error.Message); }
        catch (Exception) when (confirmed) { }

        source.ConfirmUserStateChange();
        return new(entry, ItemBatchStatus.Succeeded, data,
            PlaylistId: command == ItemCommand.AddToPlaylist ? options?.PlaylistId : null);
    }
}
