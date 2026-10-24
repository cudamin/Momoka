using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Services;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 飞牛影视「帐号 - 修改密码」。官方 0.9.8 的帐号页走 v2，密码为 SHA-256；成功后返回一个新令牌，
/// 这里连同已保存的密码一起换掉，否则下一次自动重登仍拿旧密码去试。
/// </summary>
public sealed partial class FnNasPasswordViewModel : FnNasSessionViewModel
{
    private long _editVersion;
    private ISettingsPersistence? _persistence;
    private EventHandler? _saveStateChanged;
    private object? _saveVisit;
    private bool _noticeTracksLocalSave;

    [ObservableProperty] public partial string NewPassword { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnsavedVisibility))]
    [NotifyPropertyChangedFor(nameof(CanRetrySaveLocal))]
    [NotifyCanExecuteChangedFor(nameof(RetrySaveLocalCommand))]
    public partial bool HasUnsavedChanges { get; set; }

    public Visibility UnsavedVisibility => Show(Live && HasUnsavedChanges);
    public bool CanRetrySaveLocal => CanUse && HasUnsavedChanges && _persistence is not null;
    public bool CanSave => CanUse && _persistence is not null
        && !string.IsNullOrEmpty(NewPassword) && !string.IsNullOrEmpty(ConfirmPassword);
    protected override string DeniedTitle => "需要连接飞牛影视";
    protected override string DeniedMessage => "请在服务器中连接飞牛影视后打开帐号设置。";

    internal void Attach(EmbySession session, ISettingsPersistence persistence, Action<Action> dispatch, string? sourceKey = null)
    {
        base.Attach(session, sourceKey);
        _persistence = persistence;
        var visit = _saveVisit = new object();
        // Store 通知可来自会话的后台保存；闭包固定本次订阅，迟到回调不能更新重附加的新页面。
        _saveStateChanged = (_, _) => dispatch(() =>
        {
            if (ReferenceEquals(_saveVisit, visit) && ReferenceEquals(_persistence, persistence) && Live)
                SyncSaveState();
        });
        persistence.SaveStateChanged += _saveStateChanged;
        SyncSaveState();
        BusyChanged();
    }

    internal void SyncSaveState()
    {
        HasUnsavedChanges = _persistence?.HasUnsavedChanges == true;
        if (!HasUnsavedChanges && _noticeTracksLocalSave)
        {
            _noticeTracksLocalSave = false;
            NotifyLocalSaved();
        }
    }

    protected override void ResetEditor()
    {
        NewPassword = ConfirmPassword = "";
    }

    public override Task ReloadAsync()
    {
        ClearNotice();
        SyncSaveState();
        IsReady = true;
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync()
    {
        if (!CanSave || Session is not { } session) return Task.CompletedTask;
        // 点击时即捕获两个输入；BeginLoad 的同步通知与等待确认期间都可能出现后续编辑。
        var password = NewPassword ?? "";
        var confirmation = ConfirmPassword ?? "";
        var editVersion = _editVersion;
        _noticeTracksLocalSave = false;
        return RunAsync("修改密码失败", async (scope, token) =>
        {
            if (password.Length is < 6 or > 127) throw new ArgumentException("新密码长度须为 6～127 个字符。");
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                throw new ArgumentException("两次输入的新密码不一致。");

            await session.ChangeFnNasPasswordAsync(scope, password, token);
            if (!Current(token)) return;
            var hasNewDraft = editVersion != _editVersion;
            if (!hasNewDraft) NewPassword = ConfirmPassword = "";
            SyncSaveState();
            if (HasUnsavedChanges)
            {
                _noticeTracksLocalSave = true;
                Notify("密码已修改，本地尚未保存", "服务器已确认本次密码修改；本地设置保存失败，请点击「重试本地保存」，不会再次修改服务器密码。"
                    + (hasNewDraft ? "当前新输入仍是未提交的草稿。" : ""), InfoBarSeverity.Warning);
            }
            else if (hasNewDraft)
                Notify("本次密码已修改", "本次提交的密码已更新；当前新输入尚未保存，请点击「保存修改」再次提交。", InfoBarSeverity.Warning);
            else
                Notify("密码已修改", "飞牛影视帐号密码已更新，下次登录请使用新密码。", InfoBarSeverity.Success);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRetrySaveLocal))]
    private void RetrySaveLocal()
    {
        if (!CanRetrySaveLocal || _persistence is not { } persistence) return;
        var token = BeginLoad();
        try
        {
            // Busy 的同步通知可能离页、改默认来源或重附加；本地保存也守住本次票。
            if (!Current(token) || !ReferenceEquals(_persistence, persistence)) return;
            // 只保存当前内存文档；无 password API、来源恢复或 Remember 修改。
            persistence.TrySave();
            if (!Current(token) || !ReferenceEquals(_persistence, persistence)) return;
            SyncSaveState();
            // 其它观察者可能已在原失败通知内成功保存；只以 Store 当前状态说明是否仍欠项。
            _noticeTracksLocalSave = HasUnsavedChanges;
            if (!HasUnsavedChanges) NotifyLocalSaved();
            else Notify("本地尚未保存", "当前内存设置仍保留。请检查磁盘空间和目录权限后重试本地保存；不会再次修改服务器密码，也不会提交当前新输入。", InfoBarSeverity.Warning);
        }
        finally { EndLoad(token); }
    }

    private void NotifyLocalSaved()
    {
        var hasDraft = !string.IsNullOrEmpty(NewPassword) || !string.IsNullOrEmpty(ConfirmPassword);
        Notify("本地设置已保存", hasDraft ? "已将当前内存设置保存到本地；当前新输入尚未提交，重试本地保存没有修改服务器密码。"
            : "已将当前内存设置保存到本地，没有再次修改服务器密码。", hasDraft ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }

    partial void OnNewPasswordChanged(string value) => PasswordEdited();
    partial void OnConfirmPasswordChanged(string value) => PasswordEdited();

    private void PasswordEdited()
    {
        _editVersion++;
        BusyChanged();
    }

    protected override void BusyChanged()
    {
        base.BusyChanged();
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRetrySaveLocal));
        OnPropertyChanged(nameof(UnsavedVisibility));
        RetrySaveLocalCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel()
    {
        var persistence = _persistence;
        var handler = _saveStateChanged;
        _saveVisit = null;
        _persistence = null;
        _saveStateChanged = null;
        _noticeTracksLocalSave = false;
        if (persistence is not null && handler is not null) persistence.SaveStateChanged -= handler;
        HasUnsavedChanges = false;
        base.Cancel();
        NewPassword = ConfirmPassword = "";
        BusyChanged();
    }

    public override void Dispose()
    {
        Cancel();
        base.Dispose();
    }
}
