namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>
    /// 读取关联预告的独立媒体条目。Emby 4.10.1 和 Jellyfin v10.10.7 的官方客户端/控制器均返回裸数组；
    /// Jellyfin 12 的顶层路由与 UserId 查询由既有 <c>Url</c> 转换。不要用正片编号替代这些条目的编号播放。
    /// </summary>
    public Task<List<EmbyItem>> GetLocalTrailersAsync(string itemId, CancellationToken cancellationToken)
    {
        if (!ItemTrailers.Supports(Connection.ServerKind)) throw new NotSupportedException("当前服务器不支持关联预告片");
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        return http.GetJsonAsync<List<EmbyItem>>(
            Url($"Users/{Connection.UserId}/Items/{Uri.EscapeDataString(itemId)}/LocalTrailers"), Context, cancellationToken);
    }
}
