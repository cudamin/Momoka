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
    protected EmbySessionScope? Scope { get; private set; }
    protected EmbySession? Session { get; private set; }

    /// <summary>多服务器设置分组指定的来源键；null 表示沿用当前来源。</summary>
    internal string? SourceKey { get; private set; }

    public bool CanUse => Live && !Busy;

    /// <summary>设置页用它判断内嵌的原生页是不是还属于这台飞牛服务器 / 这个账号。</summary>
    internal bool IsCurrentSession => Scope is { IsAvailable: true } scope && scope.Connection.IsFnNas
        && (SourceKey is not null || scope.IsCurrent);
    protected bool Live => _active && Scope is { IsAvailable: true } scope && scope.Connection.IsFnNas && Allowed(scope)
        && (SourceKey is not null || scope.IsCurrent);
    public Visibility ContentVisibility => Show(Live);

    /// <summary>当前账号能不能打开这一页；帐号页不限，管理页要求管理员。</summary>
    protected virtual bool Allowed(EmbySessionScope scope) => true;
    protected virtual string DeniedTitle => "";
    protected virtual string DeniedMessage => "";
    protected virtual InfoBarSeverity DeniedSeverity => InfoBarSeverity.Informational;

    internal void Attach(EmbySession session, string? sourceKey = null)
    {
        Cancel();
        _active = true;
        Session = session;
        SourceKey = sourceKey;
        Scope = sourceKey is { Length: > 0 }
            ? session.Sources.FirstOrDefault(source => string.Equals(source.Key, sourceKey, StringComparison.Ordinal))
            : session.IsSignedIn ? session.Capture() : null;
        ResetEditor();
        OnPropertyChanged(nameof(ContentVisibility));
        BusyChanged();
        if (!Live)
        {
            IsReady = true;
            if (DeniedTitle.Length > 0) Notify(DeniedTitle, DeniedMessage, DeniedSeverity);
        }
    }

    protected virtual void ResetEditor() { }
    protected bool Current(CancellationToken token) => Live && IsCurrent(token);

    protected async Task RunAsync(string failure, Func<EmbySessionScope, CancellationToken, Task> action)
    {
        if (!CanUse || Scope is not { } scope) return;
        var token = BeginLoad();
        try
        {
            scope.ThrowIfNotCurrent();
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
        finally { EndLoad(token); }
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
        base.Cancel();
        OnPropertyChanged(nameof(ContentVisibility));
    }
}
