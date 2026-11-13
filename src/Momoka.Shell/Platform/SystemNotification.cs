using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Momoka.Diagnostics;

namespace Momoka.Shell.Platform;

/// <summary>
/// 系统通知（Windows 通知中心）—— 2026-11-13 用户令「订阅失败时使用系统通知」。
/// <para>
/// 订阅失败不该只在页面上留一行字：那条提示会随着切换页面、收起窗口一起消失，而订阅结果恰恰是用户离开
/// 窗口之后还关心的事。这里只做「弹一条通知」这一件事，成功路径不打扰用户。
/// </para>
/// <para>
/// 未打包应用同样可用：Windows App SDK 的 <c>Register()</c> 自己写 COM 激活与 AUMID，不必手配清单
/// （官方 .NET 指南明确说明未打包应用由 <c>Register</c> 代劳）。反过来，提权运行时系统不支持通知，注册或发送
/// 都可能抛异常，所以每一步都吞掉失败只记一行日志 —— 通知送不出去，不能让订阅本身跟着失败。
/// </para>
/// <para>
/// 只在正式启动时注册（<see cref="Register"/>），自检、探针、截图这些工具运行不注册：
/// 它们的假服务器会故意制造订阅失败，那不是用户要看的通知，不该弹到真实桌面上。
/// </para>
/// </summary>
internal static class SystemNotification
{
    private const string Category = "系统通知";
    private static bool _registered;

    /// <summary>把本进程登记成通知的来源。工具运行不调它，之后的 <see cref="Show"/> 就什么也不做。</summary>
    internal static void Register()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception error) { Log.Warn(Category, "注册系统通知失败；本次运行不再弹通知", error); }
    }

    /// <summary>退出时撤掉登记，免得留下指向一个不在的进程的激活项。</summary>
    internal static void Unregister()
    {
        if (!_registered) return;
        _registered = false;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception error) { Log.Warn(Category, "撤销系统通知注册失败", error); }
    }

    /// <summary>
    /// 弹一条纯告知的通知。没有注册（工具运行）或系统拒绝时静默返回 —— 调用方已经在页面上说过同一件事了。
    /// </summary>
    internal static void Show(string title, string message)
    {
        if (!_registered || message.Length == 0) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception error) { Log.Warn(Category, $"发送系统通知失败：{title}", error); }
    }
}
