namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>
    /// Emby 4.10.1 官方客户端使用用户路径与 Fields；Jellyfin 10.10.7 的正式控制器和
    /// 新版规格均为顶层路径与 UserId 查询。两者返回独立媒体裸数组，不使用 AdditionalParts。
    /// </summary>
    public Task<List<EmbyItem>> GetSpecialFeaturesAsync(string itemId, CancellationToken cancellationToken)
    {
        if (Connection.ServerKind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin))
            throw new NotSupportedException("当前服务器不支持花絮与特典");
        if (!ItemExtras.IsItemId(itemId)) throw new ArgumentException("无效的媒体编号", nameof(itemId));
        if (!ItemExtras.IsItemId(Connection.UserId)) throw new ArgumentException("无效的用户编号");
        var id = Uri.EscapeDataString(itemId);
        var uri = Connection.IsJellyfin
            ? Url($"Items/{id}/SpecialFeatures", ("UserId", Connection.UserId))
            : Url($"Users/{Uri.EscapeDataString(Connection.UserId)}/Items/{id}/SpecialFeatures", ("Fields", EmbyFields.Files));
        return http.GetJsonAsync<List<EmbyItem>>(uri, Context, cancellationToken);
    }
}
