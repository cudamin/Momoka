using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 飞牛影视帐号页与服务器管理页共用的登录身份、请求代次与离页取消边界。
/// 帐号页（改密码、播放偏好）任何已登录的飞牛账号都能打开；服务器管理页在 <see cref="Allowed"/> 上再补一道管理员门。
/// </summary>
public abstract partial class FnNasSessionViewModel : PageViewModel
{
    private bool _active;
    private bool _disposed;
    private SynchronizationContext? _context;
    private int _permissionVisit;
    private bool _permissionNotice;
    // 令牌与权限代次一起发布，后台通知不能把前一操作的代次误配到刚开始的新请求上。
    private sealed record PermissionOperation(CancellationTokenSource Cancellation, CancellationToken Token,
        CancellationToken LoadToken, long Revision);
    private PermissionOperation? _permissionOperation;
    protected EmbySessionScope? Scope { get; private set; }
    protected EmbySession? Session { get; private set; }

    /// <summary>多服务器设置分组指定的来源键；null 表示沿用当前来源。</summary>
    internal string? SourceKey { get; private set; }

    protected bool IsDisposed => _disposed;
    public bool CanUse => Live && !Busy;

    /// <summary>设置页用它判断内嵌的原生页是不是还属于这台飞牛服务器 / 这个账号。</summary>
    internal bool IsCurrentSession => Scope is { IsAvailable: true } scope && scope.Connection.IsFnNas
        && (SourceKey is not null || scope.IsCurrent);
    protected bool Live => _active && Scope is { IsAvailable: true } scope && scope.Connection.IsFnNas && Allowed(scope)
        && (SourceKey is not null || scope.IsCurrent);
    public Visibility ContentVisibility => Show(Live);

    /// <summary>当前账号能不能打开这一页；帐号页不限，管理页要求管理员。</summary>
    protected virtual bool Allowed(EmbySessionScope scope) => true;

    // 帐号确认/保存不受管理员权限控制，保持原提交语义；只有管理页跟随权限快照作废在途工作。
    protected virtual bool TracksPermissions => false;
    protected virtual string DeniedTitle => "";
    protected virtual string DeniedMessage => "";
    protected virtual InfoBarSeverity DeniedSeverity => InfoBarSeverity.Informational;

    internal void Attach(EmbySession session, string? sourceKey = null)
    {
        if (_disposed) return;
        Cancel();
        _active = true;
        _context = SynchronizationContext.Current;
        _permissionNotice = false;
        Session = session;
        SourceKey = string.IsNullOrEmpty(sourceKey) ? null : sourceKey;
        Scope = SourceKey is not null
            ? session.Sources.FirstOrDefault(source => string.Equals(source.Key, SourceKey, StringComparison.Ordinal))
            : session.IsSignedIn ? session.Capture() : null;
        if (TracksPermissions)
        {
            session.PermissionsChanged += OnPermissionsChanged;
            session.SourcesChanged += OnSourcesChanged;
        }
        ResetEditor();
        OnPropertyChanged(nameof(ContentVisibility));
        BusyChanged();
        if (!Live)
        {
            IsReady = true;
            if (DeniedTitle.Length > 0)
            {
                Notify(DeniedTitle, DeniedMessage, DeniedSeverity);
                _permissionNotice = TracksPermissions;
            }
        }
    }

    protected virtual void ResetEditor() { }
    protected bool Current(CancellationToken token) => Live && !token.IsCancellationRequested
        && (Volatile.Read(ref _permissionOperation) is { } operation && operation.Token == token
            ? IsCurrent(operation.LoadToken) && Scope?.PermissionRevision == operation.Revision
            : IsCurrent(token));

    // 显式同身份重新连接走既有SourcesChanged，而自动更新走PermissionsChanged；两者只重算本页权限。
    private void OnSourcesChanged(object? sender, EventArgs args)
    {
        if (Scope is { } scope) OnPermissionsChanged(sender, scope);
    }

    private void OnPermissionsChanged(object? sender, EmbySessionScope changed)
    {
        var visit = _permissionVisit;
        var scope = Scope;
        if (!_active || !ReferenceEquals(sender, Session) || scope is null
            || !scope.IsAvailable || !changed.IsAvailable || !string.Equals(scope.Key, changed.Key, StringComparison.Ordinal)) return;

        // 先取消管理请求：UI队列尚未处理时也不能继续写；不撤销身份或播放寿命。
        var operation = Volatile.Read(ref _permissionOperation);
        try
        {
            if (operation is not null && operation.Revision != scope.PermissionRevision) operation.Cancellation.Cancel();
        }
        catch (ObjectDisposedException) { } // 完成与通知并发时，已释放的请求不再需要取消。
        catch (AggregateException error) { Momoka.Diagnostics.Log.Warn("fnnas", "取消旧权限请求失败", error); }

        void Apply()
        {
            if (!_active || visit != _permissionVisit || !ReferenceEquals(sender, Session) || !ReferenceEquals(scope, Scope)) return;
            // 排队期间可以开始一趟新权限下的读取；旧通知不能取消或标完那趟新工作。
            if (_permissionOperation is { } current && current.Revision != scope.PermissionRevision) base.Cancel();
            OnPropertyChanged(nameof(ContentVisibility));
            BusyChanged();
            if (!Busy) IsReady = true;
            if (!Live && DeniedTitle.Length > 0)
            {
                Notify(DeniedTitle, DeniedMessage, DeniedSeverity);
                _permissionNotice = true;
            }
            else if (_permissionNotice)
            {
                ClearNotice();
                _permissionNotice = false;
            }
        }

        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            // Program显式安装DispatcherQueueSynchronizationContext；这里仅非阻塞Post，没有Send/等待或优先级。
            // 本项目不使用JTF，替换为同步调度反而破坏后台通知与UI队列的边界；专项核验线程与旧visit隔离。
#pragma warning disable VSTHRD001
            context.Post(_ => Apply(), null);
#pragma warning restore VSTHRD001
        }
        else Apply();
    }

    protected async Task RunAsync(string failure, Func<EmbySessionScope, CancellationToken, Task> action)
    {
        if (!CanUse || Scope is not { } scope) return;
        var revision = TracksPermissions ? scope.PermissionRevision : 0;
        var loadToken = BeginLoad();
        using var cancellation = TracksPermissions ? CancellationTokenSource.CreateLinkedTokenSource(loadToken) : null;
        var token = cancellation?.Token ?? loadToken;
        var operation = cancellation is null ? null : new PermissionOperation(cancellation, token, loadToken, revision);
        if (operation is not null) Volatile.Write(ref _permissionOperation, operation);
        try
        {
            scope.ThrowIfNotAvailable();
            if (SourceKey is null) scope.ThrowIfNotCurrent();
            // 首次CanUse读取后、BeginLoad同步通知内均可重入降权；发出前再核当前权限与本次票。
            if (TracksPermissions && !Current(token)) return;
            await action(scope, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(token))
            {
                Momoka.Diagnostics.Log.Warn("fnnas", failure, error);
                Report(failure, error);
            }
        }
        finally
        {
            EndLoad(loadToken);
            if (operation is not null) Interlocked.CompareExchange(ref _permissionOperation, null, operation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUse))]
    private Task RefreshAsync() => ReloadAsync();

    protected override void BusyChanged()
    {
        OnPropertyChanged(nameof(CanUse));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel()
    {
        _active = false;
        _permissionVisit++;
        if (Session is { } session)
        {
            session.PermissionsChanged -= OnPermissionsChanged;
            session.SourcesChanged -= OnSourcesChanged;
        }
        _context = null;
        base.Cancel();
        OnPropertyChanged(nameof(ContentVisibility));
    }

    public override void Dispose()
    {
        _disposed = true;
        Cancel();
        base.Dispose();
    }
}
