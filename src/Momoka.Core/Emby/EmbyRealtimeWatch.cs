using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>
/// 主页那一条实时通道的账：给每一个可用来源（<see cref="EmbySessionScope"/>）各开一条
/// <see cref="EmbyRealtimeConnection"/>，收到 <c>LibraryChanged</c> 时把来源的钥匙报上来。
/// <para>
/// 跟着页面的寿命走：<c>HomePage</c> 导航进来时 <see cref="Attach"/>、离开时 <see cref="Release"/>。比 Emby
/// Theater 收得紧 —— Theater 是全局常驻（登录成功就连、整个 app 生命周期不断，见
/// <c>connectionmanager.afterConnected</c>），因为它的播放器状态也走这条通道；Momoka 要的只是「主页上有没有
/// 新入库」，页都不在了就没人接这个结果，所以连着的一整段时间里至少有一个明显的好处：**不占连接**。
/// </para>
/// <para>
/// 来源变了（多账号聚合里某个退回登录、或者换了账号）由调用方重新 <see cref="Attach"/>：这里不自己盯着
/// <c>Sources</c> 的变化，那会让「什么时候重连」变得难说清楚。
/// </para>
/// </summary>
internal sealed class EmbyRealtimeWatch : IAsyncDisposable
{
    private const string Category = "实时";

    private readonly List<EmbyRealtimeConnection> _connections = [];
    private bool _released;

    /// <summary>
    /// 按当前这批来源开通道。<paramref name="onLibraryChanged"/> 收到的是变了的那条来源的
    /// <see cref="EmbySessionScope.Key"/> —— 上层拿它决定要不要重读，还是只记个脏位。
    /// </summary>
    internal void Attach(IReadOnlyList<EmbySessionScope> sources, Action<string> onLibraryChanged)
    {
        Release();
        _released = false;

        foreach (var scope in sources)
        {
            if (!scope.IsAvailable || !EmbyRealtimeProtocol.Supports(scope.Connection.ServerKind)) continue;

            var connection = new EmbyRealtimeConnection(scope, onLibraryChanged);
            _connections.Add(connection);
            connection.Start();
        }

        if (_connections.Count > 0) Log.Info(Category, $"主页实时通道已开启（{_connections.Count} 个来源）");
    }

    /// <summary>全部收掉。页面离开时调；重复调没有副作用。</summary>
    internal void Release()
    {
        if (_released) return;
        _released = true;

        var connections = _connections.ToArray();
        _connections.Clear();
        if (connections.Length == 0) return;
        foreach (var connection in connections) connection.Cancel();

        Log.Info(Category, $"主页实时通道已关闭（{connections.Length} 个来源）");

        // 不 await：Release 挂在导航离开那条同步路径上（HomePage.Release），等一圈网络收尾会把离开卡住。
        // 每条连接自己的取消令牌已经发了，收尾在后台完成。
        _ = Task.Run(async () =>
        {
            foreach (var connection in connections)
            {
                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    Log.Debug(Category, $"实时通道收尾时出错（{error.GetType().Name}）");
                }
            }
        });
    }

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }
}
