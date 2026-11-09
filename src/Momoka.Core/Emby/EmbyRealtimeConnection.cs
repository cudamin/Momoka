using System.Net.WebSockets;
using System.Text;
using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>
/// 一条来源的原生库通知连接，收到事件后由首页核对数据。
/// Emby/Jellyfin 接 LibraryChanged，Plex 接库 timeline/status；不订阅播放状态或扫描进度。
/// URL 无令牌，凭据只走请求头；断线退避重连，页面退出后取消。
/// 2026-11-10 实服 Emby/Jellyfin 均经头部握手 101 并保持 150 秒；未安排真实入库。
/// </summary>
internal sealed class EmbyRealtimeConnection : IAsyncDisposable
{
    private const string Category = "实时";

    /// <summary>一帧最多收这么大。一条 <c>LibraryChanged</c> 只有几百字节，这个数只是防呆。</summary>
    private const int BufferBytes = 16 * 1024;

    /// <summary>连上之后多久没收到任何东西就当这条连接死了，重来。服务器不发消息是正常的（见类型注释），所以这只对半死不活的连接生效。</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private readonly EmbySessionScope _scope;
    private readonly Action<string> _onLibraryChanged;
    private readonly CancellationTokenSource _life;
    private readonly EmbyRealtimeBackoff _backoff = new();
    private Task? _loop;

    internal EmbyRealtimeConnection(EmbySessionScope scope, Action<string> onLibraryChanged)
    {
        _scope = scope;
        _onLibraryChanged = onLibraryChanged;
        _life = CancellationTokenSource.CreateLinkedTokenSource(scope.Lifetime);
    }

    /// <summary>开始跑这一条的连接循环。返回之后循环在后台自己转，<see cref="DisposeAsync"/> 收。</summary>
    internal void Start()
    {
        if (_loop is not null) return;
        var token = _life.Token;
        _loop = Task.Run(() => RunAsync(token));
    }

    /// <summary>
    /// 连接 → 收消息 → 断了退避重来，直到 <see cref="DisposeAsync"/>。
    /// <para>
    /// 这一圈里**不抛异常出去**：一条通道连不上不该把整个循环打死、也不该让调用的那一头看到红。每一轮失败只
    /// 记一条 Debug 日志，避免连接故障反复刷屏。
    /// </para>
    /// </summary>
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ListenOnceAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // Socket 异常可能携带握手信息；只记异常类型，不回显任何 URL 或鉴权头。
                Log.Debug(Category, $"{_scope.DisplayName} 实时通道断开（{error.GetType().Name}），将重连");
            }

            if (token.IsCancellationRequested) return;

            var wait = _backoff.Next();
            try
            {
                await Task.Delay(wait, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>连一次、收到断开为止。正常返回表示「连上了然后又断了」，异常表示「这一趟没连上」。</summary>
    private async Task ListenOnceAsync(CancellationToken token)
    {
        // 不能读捕获时的 Connection 快照：静默重登后该来源的 Client 已经持有新令牌。
        var connection = _scope.CurrentConnection;
        var handshake = EmbyRealtimeProtocol.Handshake(connection);
        // 系统默认握手会跟随跳转，并把 Emby 自定义鉴权头带到下一站。
        // 不自动换端点：同源、跨源跳转都拒绝，仍由原地址退避重连。
        // 保留默认代理及禁 Cookie／不复用握手连接的行为，只收紧重定向边界。
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.Zero
        });
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        foreach (var header in handshake.Headers) socket.Options.SetRequestHeader(header.Key, header.Value);

        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(handshake.Url, invoker, connect.Token).ConfigureAwait(false);
        }

        // 连上了：退避归零。下一次断线从头来（见 EmbyRealtimeBackoff.Reset）。
        _backoff.Reset();
        Log.Info(Category, $"{_scope.DisplayName} 实时通道已连接");

        await using var heartbeat = connection.IsJellyfin ? new JellyfinRealtimeKeepAlive(socket, token) : null;

        var buffer = new byte[BufferBytes];
        // UTF-8 can finish a code point begun in the preceding receive. Keep the decoder across
        // fragments, and leave one extra character of room for that carried prefix.
        var characters = new char[Encoding.UTF8.GetMaxCharCount(BufferBytes)];
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(IdleTimeout);

            EmbyRealtimeMessage? frame = null;
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(buffer, idle.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    frame ??= new EmbyRealtimeMessage(result.MessageType);
                    if (!frame.TryAppend(buffer.AsSpan(0, result.Count), characters, result.EndOfMessage))
                    {
                        // Do not drain an unending message or wait for a close handshake. Abort before
                        // decoding/appending the excess, then use the existing disposal and backoff.
                        socket.Abort();
                        throw new WebSocketException("实时消息超过容量上限");
                    }
                }
                while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // 十分钟一声不吭。服务器不发消息是正常的，所以这里不当成错误 —— 只当这条连接该换了。
                throw new WebSocketException("长时间没有收到任何数据");
            }

            token.ThrowIfCancellationRequested();
            // The server protocol is text JSON. Still count binary fragments above, but never turn
            // binary data (even JSON-looking bytes) into a library refresh.
            if (frame?.Text is not { } text) continue;
            if (heartbeat is not null) await heartbeat.ReceiveAsync(text).ConfigureAwait(false);
            var changed = connection.IsPlex ? Momoka.Plex.PlexRealtimeProtocol.IsLibraryChanged(text)
                : EmbyRealtimeProtocol.IsLibraryChanged(EmbyRealtimeProtocol.ReadMessageType(text));
            if (!changed) continue;

            Log.Info(Category, $"{_scope.DisplayName} 收到库变更通知，主页将核对新入库");
            token.ThrowIfCancellationRequested();
            _onLibraryChanged(_scope.Key);
        }
    }

    internal void Cancel()
    {
        try
        {
            _life.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经收过了。
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Log.Debug(Category, $"实时通道收尾时出错（{error.GetType().Name}）");
            }
        }

        _life.Dispose();
    }
}

/// <summary>The bounded assembly of one message, not one receive or one WebSocket fragment.</summary>
internal sealed class EmbyRealtimeMessage
{
    // Production consumes only the LibraryChanged envelope, not Data. Current fixtures are tens of
    // bytes and the existing business payload is documented as hundreds; 1 MiB leaves room for large
    // item-ID batches without trusting a server's never-ending or arbitrarily large message.
    internal const int MaximumBytes = 1 << 20;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Decoder? _decoder;
    private readonly StringBuilder? _text;
    private int _bytes;

    internal EmbyRealtimeMessage(WebSocketMessageType type)
    {
        if (type != WebSocketMessageType.Text) return;
        _decoder = Utf8.GetDecoder();
        _text = new StringBuilder();
    }

    internal int Bytes => _bytes;
    internal string? Text => _text?.ToString();

    internal bool TryAppend(ReadOnlySpan<byte> bytes, Span<char> characters, bool endOfMessage)
    {
        if (bytes.Length > MaximumBytes - _bytes) return false;
        _bytes += bytes.Length;
        if (_decoder is null) return true;
        var count = _decoder.GetChars(bytes, characters, flush: endOfMessage);
        _text!.Append(characters[..count]);
        return true;
    }
}
