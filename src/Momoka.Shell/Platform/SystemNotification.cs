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
/// Windows App SDK 为未打包应用写入通知身份和 COM 激活项。注册或发送出错时只记录日志，
/// 通知失败不改变订阅或打格子的结果。
/// </para>
/// <para>
/// 只在正式启动时注册（<see cref="Register"/>），自检、探针、截图这些工具运行不注册：
/// 它们的假服务器会故意制造订阅失败，那不是用户要看的通知，不该弹到真实桌面上。
/// </para>
/// </summary>
internal static class SystemNotification
{
    private const string Category = "系统通知";
    private const int ModuleNotFound = unchecked((int)0x8007007E);
    private const int NoPackageIdentity = unchecked((int)0x80073D54);
    private static bool _registered;
    private static bool _sendOnly;

    /// <summary>把本进程登记成通知的来源。工具运行不调它，之后的 <see cref="Show"/> 就什么也不做。</summary>
    internal static void Register()
    {
        if (_registered || _sendOnly) return;
        AppNotificationManager? manager = null;
        try
        {
            if (!AppNotificationManager.IsSupported()) return;
            manager = AppNotificationManager.Default;
            manager.Register();
            _registered = true;
        }
        catch (Exception error) when (manager is not null && error.HResult == ModuleNotFound
            && IsUnpackagedRuntimeWithoutInsights())
        {
            // Windows App SDK 2.5.1 (13160d54), dev/AppNotifications/AppNotificationManager.cpp:
            // RegisterUnpackagedApp writes the identity before checking for the aggregate runtime.
            // Local Show posts through the OS directly, independently of COM activation registration.
            _sendOnly = true;
            Log.Warn(Category, "系统通知已保留发送功能，点击激活注册失败", error);
        }
        catch (Exception error) { Log.Warn(Category, "注册系统通知失败；本次运行不再弹通知", error); }
    }

    private static bool IsUnpackagedRuntimeWithoutInsights()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll"))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.Insights.Resource.dll")))
            return false;

        try { return Windows.ApplicationModel.Package.Current is null; }
        catch (Exception error) when (error.HResult == NoPackageIdentity) { return true; }
    }

    /// <summary>退出时撤掉登记，免得留下指向一个不在的进程的激活项。</summary>
    internal static void Unregister()
    {
        _sendOnly = false;
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
        if ((!_registered && !_sendOnly) || message.Length == 0) return;
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
