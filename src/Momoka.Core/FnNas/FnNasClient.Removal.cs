using System.Text.Json.Nodes;

namespace Momoka.FnNas;

public sealed partial class FnNasClient
{
    /// <summary>只移出媒体库；空版本清单表示整个条目，点名版本时只移出该文件。</summary>
    public Task RemoveItemFromLibraryAsync(FnNasConnection c, string itemGuid, IReadOnlyList<string> mediaGuids,
        CancellationToken ct = default, Action? confirmed = null)
    {
        _ = FnNasEndpoint.Segment(itemGuid);
        ArgumentNullException.ThrowIfNull(mediaGuids);
        foreach (var guid in mediaGuids) _ = FnNasEndpoint.Segment(guid);
        return ConfirmedAdminActionAsync(c, HttpMethod.Delete, "api/v1/item/" + FnNasEndpoint.Segment(itemGuid),
            FnNasItemActions.Delete(deleteFile: false, mediaGuids), ct, confirmed);
    }

    /// <summary>
    /// 官方文件目录是管理面；根只发 mdb_guid，下钻另发 fv_guid。没有全库已移除列表接口，
    /// 因此保留目录导航，只返回导航行和 blacked=1 的恢复项，不加入正常媒体或播放入口。
    /// </summary>
    public async Task<FnNasRemovedFileDirectory> GetRemovedFileDirectoryAsync(FnNasConnection c, string libraryGuid,
        string? folderGuid = null, CancellationToken ct = default)
    {
        _ = FnNasEndpoint.Segment(libraryGuid);
        if (folderGuid is not null) _ = FnNasEndpoint.Segment(folderGuid);
        await RequireAdminAsync(c, ct).ConfigureAwait(false);
        var query = "?mdb_guid=" + Uri.EscapeDataString(libraryGuid);
        if (folderGuid is not null) query += "&fv_guid=" + Uri.EscapeDataString(folderGuid);
        var data = await ObjectAsync(c, HttpMethod.Get, "api/v1/fileViewAdmin/list" + query, null, ct).ConfigureAwait(false);
        return FnNasRemovedFileDirectory.From(data);
    }

    /// <summary>解除官方黑名单并请求重新入库。成功只表示服务端接受，扫描与页面刷新另行读取。</summary>
    public Task RestoreRemovedFileAsync(FnNasConnection c, string libraryGuid, string path, CancellationToken ct = default, Action? confirmed = null)
    {
        _ = FnNasEndpoint.Segment(libraryGuid);
        ValidateRestorePath(path);
        return ConfirmedAdminActionAsync(c, HttpMethod.Post, "api/v1/scrap/removeFromBlackByPath", new JsonObject
        {
            ["mdb_guid"] = libraryGuid,
            ["path"] = path
        }, ct, confirmed);
    }

    private async Task ConfirmedAdminActionAsync(FnNasConnection c, HttpMethod method, string path, JsonObject body,
        CancellationToken ct, Action? confirmed)
    {
        await RequireAdminAsync(c, ct).ConfigureAwait(false);
        c.ThrowIfNotCurrent();
        ct.ThrowIfCancellationRequested();
        if (confirmed is null) await ActionAsync(c, method, path, body, ct).ConfigureAwait(false);
        else _ = await http.SendConfirmedActionAsync(method, FnNasEndpoint.Api(c.ApiBase, path), body, c.AccessToken, confirmed, ct).ConfigureAwait(false);
    }

    private static void ValidateRestorePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Any(char.IsControl) || path.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("飞牛影视恢复路径无效", nameof(path));
    }
}
