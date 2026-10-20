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
/// URL 只携带 <c>deviceId</c>；令牌在进程内通过产品自己的请求头传递。
/// </para>
/// </summary>
public static class EmbyRealtimeProtocol
{
    public static bool Supports(MediaServerKind kind) => kind is MediaServerKind.Emby or MediaServerKind.Jellyfin;

    /// <summary>实时 URL 只包含端点和设备号。凭据通过 <see cref="Handshake"/> 的请求头传递。</summary>
    public static Uri Build(Uri apiBase, string deviceId, MediaServerKind kind = MediaServerKind.Emby)
    {
        ArgumentNullException.ThrowIfNull(apiBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!Supports(kind)) throw new NotSupportedException("该服务器没有受支持的实时通道");
        var scheme = apiBase.Scheme switch
        {
            "https" => "wss",
            "http" => "ws",
            _ => throw new ArgumentException("实时通道只支持 http/https", nameof(apiBase))
        };
        var basePath = apiBase.AbsolutePath.TrimEnd('/');
        // 本轮只修凭据与生命周期。Emby 现有端点别名仍需真实服务器取证，不借鉴权修复猜改路径。
        var endpoint = kind == MediaServerKind.Jellyfin ? "socket" : "embywebsocket";
        return new UriBuilder(apiBase)
        {
            Scheme = scheme,
            Path = $"{basePath}/{endpoint}",
            Query = "deviceId=" + Uri.EscapeDataString(deviceId),
            Fragment = "",
            UserName = "",
            Password = ""
        }.Uri;
    }

    /// <summary>复用各产品已支持的头部鉴权，不使用默认停用的 Jellyfin api_key 查询凭据。</summary>
    internal static (Uri Url, List<KeyValuePair<string, string>> Headers) Handshake(EmbyConnection connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.AccessToken);
        if (connection.AccessToken.Any(char.IsControl)) throw new ArgumentException("实时通道凭据无效");
        return (Build(connection.ApiBase, connection.Device.DeviceId, connection.ServerKind),
            MediaServerHeaders.Create(connection.Device, connection.AccessToken, connection.ServerKind));
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
