using System.Text.Json.Nodes;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>
/// 飞牛官方首页从 task/running 的扫描任务消失判断目录需要刷新；不是入库推送，也不轮询媒体内容。
/// 官方 TaskStore 位于顶层网页路由；这里由首页显式 Attach/Release 收窄到现有页面寿命。
/// </summary>
public sealed class FnNasTaskWatch : IAsyncDisposable
{
    private const string Category = "飞牛任务";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private static readonly object MissingAncestor = new();
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task> _delay;
    private CancellationTokenSource? _visit;
    private Task _loop = Task.CompletedTask;
    private bool _disposed;

    public FnNasTaskWatch() : this(token => Task.Delay(Interval, token)) { }

    internal FnNasTaskWatch(Func<CancellationToken, Task> delay) => _delay = delay;

    /// <summary>每个在线管理员来源一条串行循环；再次附加先取消并等待上一代读取结束。</summary>
    public void Attach(IReadOnlyList<EmbySessionScope> sources, Action<string> onDirectoryChanged)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(onDirectoryChanged);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _visit?.Cancel();
            var previous = _loop;
            var visit = new CancellationTokenSource();
            _visit = visit;
            var current = sources.Where(Eligible).DistinctBy(scope => scope.Key).ToArray();
            _loop = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    if (visit.IsCancellationRequested) return;
                    await Task.WhenAll(current.Select(scope => WatchAsync(scope, visit, onDirectoryChanged))).ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate)
                        if (ReferenceEquals(_visit, visit)) _visit = null;
                    visit.Dispose();
                }
            });
        }
    }

    /// <summary>导航离开时同步撤销；DisposeAsync 另外等待真实网络读取和循环退出。</summary>
    public void Release()
    {
        lock (_gate)
        {
            _visit?.Cancel();
            _visit = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task loop;
        lock (_gate)
        {
            _disposed = true;
            _visit?.Cancel();
            _visit = null;
            loop = _loop;
        }
        await loop.ConfigureAwait(false);
    }

    private static bool Eligible(EmbySessionScope scope) =>
        scope.Connection.IsFnNas && scope.IsAvailable && scope.IsAdministrator == true;

    private async Task WatchAsync(EmbySessionScope scope, CancellationTokenSource visit, Action<string> changed)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(visit.Token, scope.Lifetime);
        var token = lifetime.Token;
        IReadOnlyList<object?>? previous = null;
        try
        {
            while (!token.IsCancellationRequested && Eligible(scope))
            {
                var permissionRevision = scope.PermissionRevision;
                try
                {
                    var tasks = await scope.ExecuteAsync((client, ct) =>
                        client.FnNas.GetRunningTasksAsync(client.FnNasConnection, ct), token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!Eligible(scope) || scope.PermissionRevision != permissionRevision) return;
                    var current = Ancestors(tasks);
                    lock (_gate)
                    {
                        if (!ReferenceEquals(_visit, visit) || token.IsCancellationRequested || !Eligible(scope)) return;
                        if (previous is not null && HasRemoved(previous, current)) changed(scope.Key);
                        previous = current;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || !Eligible(scope)) { return; }
                catch (Exception error)
                {
                    // 失败不是空任务表。保留上次成功快照，下一次成功后再判断真实消失。
                    Log.Debug(Category, $"任务快照读取失败（{error.GetType().Name}）");
                }
                if (token.IsCancellationRequested || !Eligible(scope)) return;
                await _delay(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    /// <summary>
    /// 官方只过滤精确 TaskItemScrap 再取祖先；缺失(undefined)、null、空串和数字不混用。
    /// 未知对象/数组保留引用相等语义，不把不同响应的同形对象当同一个祖先。
    /// </summary>
    internal static IReadOnlyList<object?> Ancestors(IReadOnlyList<JsonObject> tasks)
    {
        var values = new List<object?>();
        foreach (var task in tasks)
        {
            if (task["type"] is not JsonValue type || !type.TryGetValue<string>(out var text) || text != "TaskItemScrap") continue;
            object? value = MissingAncestor;
            if (task.TryGetPropertyValue("ancestor", out var ancestor))
            {
                value = ancestor;
                if (ancestor is JsonValue scalar)
                {
                    if (scalar.TryGetValue<string>(out var name)) value = name;
                    else if (scalar.TryGetValue<bool>(out var flag)) value = flag;
                    else if (scalar.TryGetValue<double>(out var number)) value = number;
                }
            }
            if (!values.Contains(value)) values.Add(value);
        }
        return values;
    }

    internal static bool HasRemoved(IReadOnlyList<object?> previous, IReadOnlyList<object?> current) =>
        previous.Any(value => !current.Contains(value));
}
