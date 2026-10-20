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
    private Task SaveAsync() => !CanSave ? Task.CompletedTask : RunAsync("修改密码失败", async (scope, token) =>
    {
        var password = NewPassword ?? "";
        if (password.Length is < 6 or > 127) throw new ArgumentException("新密码长度须为 6～127 个字符。");
        if (!string.Equals(password, ConfirmPassword ?? "", StringComparison.Ordinal))
            throw new ArgumentException("两次输入的新密码不一致。");

        if (Session is not { } session) return;
        await session.ChangeFnNasPasswordAsync(scope, password, token);
        if (!Current(token)) return;

        NewPassword = ConfirmPassword = "";
        Notify("密码已修改", "飞牛影视帐号密码已更新，下次登录请使用新密码。", InfoBarSeverity.Success);
    });

    partial void OnNewPasswordChanged(string value) => BusyChanged();
    partial void OnConfirmPasswordChanged(string value) => BusyChanged();

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
