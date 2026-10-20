using System.Net.WebSockets;
using System.Text;
using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>
/// 一台服务器上的一条实时通道：连上 <c>/embywebsocket</c>，被动等着服务器推 <c>LibraryChanged</c>，每收到一条
/// 就喊一声 <see cref="LibraryChanged"/>。
/// <para>
/// <b>为什么有这一层。</b>主页停着不动的时候，服务器新入库的内容不会自己出现 —— 这一页只在导航进来时读一次，
/// 现在靠 <c>HomePage</c> 那只 30 秒的钟问一问（<see cref="LibraryWatch"/>）。那条路可靠，但最长有半分钟的空窗，
/// 而且每一拍都是一次实打实的请求。这条通道把空窗压到约一秒，代价是零次轮询：真机实测过，空闲 35 秒、60 秒
/// 各收到 <b>0 条</b>消息，服务器不发就是不发。
/// </para>
/// <para>
/// <b>它只是「快路径」，不是替代品。</b>30 秒那一趟留着不动（见 <c>HomeViewModel.AutoRefreshAsync</c>）：
/// 这条通道连不上、或者服务器根本没推 <c>LibraryChanged</c> 的时候，行为<b>等于今天</b> —— 不会更差。这一点是
/// 有意设计的：<c>LibraryChanged</c> 在 Momoka 这台服务器上<b>没能实证</b>（库在 NAS 上，探针够不到文件系统去投
/// 真文件），所以整条路都按「可能一次都不来」来写。
/// </para>
/// <para>
/// <b>只认 <c>LibraryChanged</c>，不订阅任何东西。</b>特别是不订阅 <c>Sessions</c>：真机实测那一类在播放期间是
/// 每 2 秒一条（90 秒 21 条），订阅它等于看片的时候主页一直在重读。Emby Theater 订阅了
/// <c>Sessions</c> / <c>ActivityLogEntry</c> / <c>ScheduledTasksInfo</c> / <c>SessionEvents</c> 四类，
/// 是因为它的播放器状态、服务端任务都靠那些事件驱动；Momoka 要的只是「有没有新入库」，所以一条都不订阅 ——
/// 不订阅的时候，这条连接在空闲和播放期间都是静默的。
/// </para>
/// <para>
/// <b>一条连接对一个 <see cref="EmbySessionScope"/>。</b>多账号聚合时每个来源各连各的：某个来源连不上不该让
/// 别的来源也失去快路径，而且各来源的令牌本来就不同。
/// </para>
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
    /// 记一条日志（Debug 档，因为它本来就是「快路径断了还有兜底」这种事，不该在 Info 里刷屏）。
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
        var handshake = EmbyRealtimeProtocol.Handshake(_scope.CurrentConnection);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        foreach (var header in handshake.Headers) socket.Options.SetRequestHeader(header.Key, header.Value);

        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(handshake.Url, connect.Token).ConfigureAwait(false);
        }

        // 连上了：退避归零。下一次断线从头来（见 EmbyRealtimeBackoff.Reset）。
        _backoff.Reset();
        Log.Info(Category, $"{_scope.DisplayName} 实时通道已连接");

        var buffer = new byte[BufferBytes];
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(IdleTimeout);

            var frame = new StringBuilder();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(buffer, idle.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    frame.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // 十分钟一声不吭。服务器不发消息是正常的，所以这里不当成错误 —— 只当这条连接该换了。
                throw new WebSocketException("长时间没有收到任何数据");
            }

            if (EmbyRealtimeProtocol.ReadMessageType(frame.ToString()) is not { } type) continue;
            if (!EmbyRealtimeProtocol.IsLibraryChanged(type)) continue;

            Log.Info(Category, $"{_scope.DisplayName} 收到 LibraryChanged，主页将核对新入库");
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
