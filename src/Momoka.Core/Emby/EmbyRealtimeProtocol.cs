namespace Momoka.Emby;

/// <summary>
/// 实时通道那一条 URL 和它上面过的消息 —— 能被单测钉住的那部分，和 <see cref="LibraryWatch"/> 分开写。
/// <para>
/// 为什么单独一层：URL 怎么拼、什么消息算「有变更」，屏上都看不出来 —— 拼错的那一版只会表现为「一直连不上」，
/// 判错的那一版只会表现为「有新东西却不刷」或者「没事乱刷」。这两件事都是能被单测钉死的，所以放在 Core 里、
/// 不碰网络。
/// </para>
/// <para>
/// 端点本身不在 <c>emby-openapi.json</c> 里（434 条路径里没有一条 socket 路径，<c>LibraryChanged</c> 在文档里
/// 出现 0 次）—— 这一层照的是 Emby Theater 的实现（<c>apiclient.js</c> 的 <c>openWebSocket</c>）：
/// <c>emby/socket</c> 换成 <c>embywebsocket</c>，<c>https</c> 换 <c>wss</c>、<c>http</c> 换 <c>ws</c>，
/// 再把 <c>api_key</c> 和 <c>deviceId</c> 拼在查询串上。
/// </para>
/// </summary>
public static class EmbyRealtimeProtocol
{
    /// <summary>
    /// 一台服务器上的实时通道地址。<paramref name="apiBase"/> 是 HTTP 的基地址（<c>EmbyConnection.ApiBase</c>），
    /// 这里只换协议、换路径、拼令牌，不做别的。
    /// <para>
    /// 令牌为空直接抛：没有令牌的握手服务器会接受，但一条消息都不推（真机实测过）—— 那样得到的是一条「活着但
    /// 永远沉默」的连接，比连不上更难查。宁可在这里就断掉。
    /// </para>
    /// </summary>
    public static Uri Build(Uri apiBase, string accessToken, string deviceId,
        MediaServerKind kind = MediaServerKind.Emby)
    {
        ArgumentNullException.ThrowIfNull(apiBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var scheme = apiBase.Scheme switch
        {
            "https" => "wss",
            "http" => "ws",
            _ => throw new ArgumentException($"实时通道只认 http/https，收到的是 {apiBase.Scheme}", nameof(apiBase))
        };

        // ApiBase 可能带反代前缀（/proxy/v 那一类），所以从它的 Path 接上去而不是从根接。
        var basePath = apiBase.AbsolutePath.TrimEnd('/');

        // 两家网页端的实时端点不同：Emby 是 emby/socket 换成的 embywebsocket；Jellyfin Web 的
        // apiclient 用 getUrl("socket")，也就是基址下的 /socket。Jellyfin 12 若停用查询串凭据，
        // 这里需要另找握手方式 —— 当前核对的是 10.10.7。
        var endpoint = kind == MediaServerKind.Jellyfin ? "socket" : "embywebsocket";
        var builder = new UriBuilder(apiBase) { Scheme = scheme, Path = $"{basePath}/{endpoint}", Query = string.Empty };

        return new Uri($"{builder.Uri.GetLeftPart(UriPartial.Path)}?api_key={Uri.EscapeDataString(accessToken)}&deviceId={Uri.EscapeDataString(deviceId)}");
    }

    /// <summary>
    /// 这条消息算不算「服务器上可能有了新东西」。<c>null</c> 或读不出来的一律不算。
    /// <para>
    /// 只认 <c>LibraryChanged</c>。<b>不认 <c>Sessions</c></b> —— 那一类在播放期间是每 2 秒一条（真机实测：
    /// 90 秒窗口里 21 条，电视一开机就变成精确的 2 秒一拍），拿它当刷新的信号，等于看片的时候主页一直在重读。
    /// 也不认 <c>RefreshProgress</c>（那是扫描进度条，扫一遍发十几条，和「有没有新东西」是两回事）。
    /// </para>
    /// <para>
    /// <c>UserDataChanged</c> 同样不算：那是「观看进度变了」——用户自己正在看的那一集，主页那几排本来就该
    /// 由播放回来的那一趟（<c>RefreshActive</c>）管，不该由这里再触发一次整页重读。
    /// </para>
    /// </summary>
    public static bool IsLibraryChanged(string? messageType) =>
        string.Equals(messageType, "LibraryChanged", StringComparison.Ordinal);

    /// <summary>
    /// 从一帧（可能是分片拼起来的）里取出 <c>MessageType</c>；不是 JSON、或者没有这个字段，返回 <c>null</c>。
    /// 只有取字段这一件事 —— 不解析 Data，因为上层只把它当信号用（见 <see cref="IsLibraryChanged"/>）。
    /// </summary>
    public static string? ReadMessageType(ReadOnlySpan<char> frame)
    {
        if (frame.IsEmpty) return null;

        try
        {
            var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(frame.ToString()));
            if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject) return null;

            while (reader.Read())
            {
                if (reader.TokenType != System.Text.Json.JsonTokenType.PropertyName) continue;
                if (!reader.ValueTextEquals("MessageType")) continue;
                return reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.String
                    ? reader.GetString()
                    : null;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        return null;
    }
}
