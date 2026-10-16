using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

/// <summary>飞牛管理页面共用的登录身份、请求代次与离页取消边界。</summary>
public abstract partial class FnNasAdminViewModel : PageViewModel
{
    private bool _active;
    private bool _authorized;
    protected EmbySessionScope? Scope { get; private set; }
    public bool CanUse => Live && !Busy;
    private bool HasIdentity => _active && Scope is { IsCurrent: true } scope && scope.Connection.IsFnNas;
    public bool CanRefresh => HasIdentity && !Busy;
    protected bool Live => HasIdentity && _authorized;
    public Visibility ContentVisibility => Show(Live);
    public Visibility RecoveryVisibility => Show(HasIdentity && !_authorized);
    public Visibility NotesVisibility => SettingRow.NotesVisibility;

    internal void Attach(EmbySession session)
    {
        Cancel();
        _active = true;
        Scope = session.IsSignedIn ? session.Capture() : null;
        _authorized = Scope?.Connection.IsAdministrator == true;
        ResetEditor();
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(RecoveryVisibility));
        OnPropertyChanged(nameof(NotesVisibility));
        BusyChanged();
        if (!Live)
        {
            IsReady = true;
            Notify("需要飞牛影视管理员账号", "请在服务器中连接飞牛影视，并使用管理员账号打开管理页面。", InfoBarSeverity.Informational);
        }
    }

    protected virtual void ResetEditor() { }
    protected static Momoka.FnNas.FnNasConnection CurrentConnection(EmbySessionScope scope, EmbyClient client) =>
        client.FnNasConnection with { IsCurrent = () => scope.IsCurrent };
    protected bool Current(CancellationToken token) => Live && IsCurrent(token);

    protected async Task RunAsync(string failure, Func<EmbySessionScope, CancellationToken, Task> action)
    {
        if (!CanRefresh || Scope is not { } scope) return;
        var token = BeginLoad();
        try
        {
            scope.ThrowIfNotCurrent();
            var user = await scope.ExecuteAsync((client, ct) => client.GetCurrentUserAsync(ct), token);
            if (!HasIdentity || !IsCurrent(token)) return;
            _authorized = user.Policy?.IsAdministrator == true;
            if (!_authorized)
            {
                Notify("需要管理员权限", "当前账号没有管理飞牛影视的权限。权限恢复后可重新检查。", InfoBarSeverity.Informational);
                return;
            }
            await action(scope, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(token)) Report(failure, error);
        }
        finally
        {
            EndLoad(token);
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(RecoveryVisibility));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => ReloadAsync();

    protected override void BusyChanged()
    {
        OnPropertyChanged(nameof(CanUse));
        OnPropertyChanged(nameof(CanRefresh));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel()
    {
        _active = false;
        base.Cancel();
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(RecoveryVisibility));
    }
}
