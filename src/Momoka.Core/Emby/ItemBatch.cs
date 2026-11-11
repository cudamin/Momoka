namespace Momoka.Emby;

public sealed record ItemBatchEntry(EmbyItem Item, CollectionMembership? Membership = null);
public enum ItemBatchStatus { Succeeded, Failed, Canceled }

/// <summary>A confirmed write and its optional read-back have separate outcomes.</summary>
public sealed record ItemBatchItemResult(ItemBatchEntry Entry, ItemBatchStatus Status,
    EmbyUserData? UserData = null, EmbyUserData? RefreshedUserData = null,
    string? Error = null, string? RefreshError = null)
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
        ItemCommand.AddToCollection, ItemCommand.RemoveFromCollection
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
            _ => false
        };
    }
}

/// <summary>Runs one captured account's independent writes. Cancel means stop before the next item.</summary>
public sealed class ItemBatchService
{
    public async Task<ItemBatchResult> ExecuteAsync(EmbySessionScope source, IReadOnlyList<ItemBatchEntry> entries,
        ItemCommand command, Func<ItemMenuPermissions> permissions, string? collectionId,
        CancellationToken cancellationToken, Action<ItemBatchItemResult>? completed = null,
        Action<ItemBatchItemResult>? refreshed = null)
    {
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
                result = await WriteAsync(source, entry, command, permissions, collectionId).ConfigureAwait(true);
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

    private static async Task<ItemBatchItemResult> WriteAsync(EmbySessionScope source, ItemBatchEntry entry,
        ItemCommand command, Func<ItemMenuPermissions> permissions, string? collectionId)
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
        return new(entry, ItemBatchStatus.Succeeded, data);
    }
}
