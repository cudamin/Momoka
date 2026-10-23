using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 飞牛影视「帐号 - 修改密码」。官方 0.9.8 的帐号页走 v2，密码为 SHA-256；成功后返回一个新令牌，
/// 这里连同已保存的密码一起换掉，否则下一次自动重登仍拿旧密码去试。
/// </summary>
public sealed partial class FnNasPasswordViewModel : FnNasSessionViewModel
{
    private long _editVersion;

    [ObservableProperty] public partial string NewPassword { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    public bool CanSave => CanUse && !string.IsNullOrEmpty(NewPassword) && !string.IsNullOrEmpty(ConfirmPassword);
    protected override string DeniedTitle => "需要连接飞牛影视";
    protected override string DeniedMessage => "请在服务器中连接飞牛影视后打开帐号设置。";

    protected override void ResetEditor()
    {
        NewPassword = ConfirmPassword = "";
    }

    public override Task ReloadAsync()
    {
        ClearNotice();
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
        return RunAsync("修改密码失败", async (scope, token) =>
        {
            if (password.Length is < 6 or > 127) throw new ArgumentException("新密码长度须为 6～127 个字符。");
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                throw new ArgumentException("两次输入的新密码不一致。");

            await session.ChangeFnNasPasswordAsync(scope, password, token);
            if (!Current(token)) return;
            if (editVersion != _editVersion)
            {
                Notify("本次密码已修改", "本次提交的密码已更新；当前新输入尚未保存，请点击「保存修改」再次提交。", InfoBarSeverity.Warning);
                return;
            }

            NewPassword = ConfirmPassword = "";
            Notify("密码已修改", "飞牛影视帐号密码已更新，下次登录请使用新密码。", InfoBarSeverity.Success);
        });
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
    }

    public override void Cancel()
    {
        base.Cancel();
        NewPassword = ConfirmPassword = "";
    }
}
