using System.Text.Json.Nodes;

namespace Momoka.Emby;

/// <summary>Scope-bound read/save boundary for the native Emby form; no HTTP belongs in the view.</summary>
public sealed class EmbyMetadataEditor(EmbySessionScope source)
{
    public async Task<EmbyMetadataDocument> LoadAsync(string itemId, CancellationToken cancellationToken)
    {
        CheckAccess();
        _ = EmbyClient.MetadataItemId(itemId);
        var item = await source.ExecuteAsync((client, token) => client.GetItemJsonAsync(itemId, token), cancellationToken)
            .ConfigureAwait(false);
        CheckAccess();
        if (EmbyMetadataEdit.Text(item, "Id") != itemId)
            throw new InvalidOperationException("服务器返回的元数据不属于正在编辑的条目");
        var info = await source.ExecuteAsync((client, token) => client.GetMetadataEditorInfoAsync(itemId, token), cancellationToken)
            .ConfigureAwait(false);
        CheckAccess();
        return new EmbyMetadataDocument(source, itemId, item, info);
    }

    /// <returns>False for a genuine no-op. Failures leave both the original document and the draft intact.</returns>
    public async Task<bool> SaveAsync(EmbyMetadataDocument document, EmbyMetadataEdit edit, CancellationToken cancellationToken)
    {
        CheckAccess();
        if (!ReferenceEquals(document.Source, source)) throw new InvalidOperationException("元数据属于另一个服务器账号");
        var item = document.CopyOriginal();
        if (!edit.ApplyTo(item)) return false;
        await source.ExecuteAsync((client, token) =>
        {
            CheckAccess();
            return client.UpdateItemAsync(document.ItemId, item, token);
        }, cancellationToken).ConfigureAwait(false);
        document.Accept(item);
        return true;
    }

    private void CheckAccess()
    {
        source.ThrowIfUnavailable();
        if (source.Connection.ServerKind != MediaServerKind.Emby) throw new NotSupportedException("当前服务器不是 Emby");
        if (source.IsAdministrator != true) throw new UnauthorizedAccessException("只有 Emby 管理员可以编辑元数据");
    }
}

public sealed class EmbyMetadataDocument
{
    private JsonObject _original;
    internal EmbySessionScope Source { get; }
    public string ItemId { get; }
    public EmbyMetadataEdit Edit => EmbyMetadataEdit.Read(_original);
    public EmbyMetadataEditorInfo Info { get; }

    internal EmbyMetadataDocument(EmbySessionScope source, string itemId, JsonObject item, EmbyMetadataEditorInfo info)
    {
        Source = source;
        ItemId = itemId;
        _original = (JsonObject)item.DeepClone();
        Info = info;
    }

    internal JsonObject CopyOriginal() => (JsonObject)_original.DeepClone();
    internal void Accept(JsonObject item) => _original = (JsonObject)item.DeepClone();
}
