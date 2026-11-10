namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>解除成员关系：Emby POST /Items/Delete，Jellyfin DELETE /Items；不删除媒体文件。</summary>
    public Task RemoveFromCollectionAsync(string collectionId, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
    {
        var path = $"Collections/{CollectionSegment(collectionId)}/Items";
        var ids = string.Join(',', itemIds.Select(CollectionId));
        if (itemIds.Count == 0) throw new ArgumentException("请选择要移出合集的媒体", nameof(itemIds));
        return Connection.IsJellyfin
            ? http.DeleteAsync(Url(path, ("Ids", ids)), Context, cancellationToken)
            : http.PostAsync(Url(path + "/Delete", ("Ids", ids)), null, Context, cancellationToken);
    }

    internal static string CollectionSegment(string value) => Uri.EscapeDataString(CollectionId(value));

    internal static string CollectionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(['/', '\\', '?', '#', ',']) >= 0)
            throw new ArgumentException("合集或媒体标识无效", nameof(value));
        return value;
    }
}
