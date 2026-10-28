namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>Choices for Emby's editor, not a product-neutral localization endpoint.</summary>
    public Task<EmbyMetadataEditorInfo> GetMetadataEditorInfoAsync(string itemId, CancellationToken cancellationToken)
    {
        if (Connection.ServerKind != MediaServerKind.Emby) throw new NotSupportedException("当前服务器不是 Emby");
        return http.GetJsonAsync<EmbyMetadataEditorInfo>(Url($"Items/{MetadataItemId(itemId)}/MetadataEditor"),
            Context, cancellationToken);
    }
    internal static string MetadataItemId(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (itemId is "." or ".." || itemId.IndexOfAny(['/', '\\', '?', '#']) >= 0)
            throw new ArgumentException("元数据操作缺少有效的条目标识", nameof(itemId));
        return Uri.EscapeDataString(itemId);
    }
}
