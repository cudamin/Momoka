using Microsoft.Win32;
using Momoka.Configuration;
using Momoka.Diagnostics;

namespace Momoka.Shell.Platform;

/// <summary>
/// 开机自启：把本程序挂到（或者从）Windows 的启动项里，并读回它此刻在不在。
/// <para>
/// 判断全在 Core 的 <see cref="StartupEntry"/>（单测钉着）；这里只做注册表那几下，和
/// <see cref="ISystemLauncher"/> 一样是「视图模型会去够的那几样平台能力」之一 —— 抽成接口是为了让离线探针换成
/// 假的：真拨一次开关会在跑探针那台机器上留下一条真的启动项。
/// </para>
/// </summary>
internal interface IStartupRegistration
{
    /// <summary>
    /// 这台机器上「开机自启」这件事办不办得到。打包安装（MSIX）之后答 false：那种身份下程序自己的数据目录
    /// 和它的启动项都归包管理，用 Run 那一格拉起来的会是一个没有包身份、因而读写另一份设置和另一份账号的
    /// 副本 —— 那不是「开机自启」，那是打开了另一个程序。办不到的功能按项目规则整个不显示。
    /// </summary>
    bool Available { get; }

    /// <summary>此刻会不会开机自启 —— 读注册表当下的事实，不是读一份记下来的偏好。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 拨开关。<paramref name="enabled"/> 为真就写上那一条（值永远是当前这个可执行文件的路径），为假就删掉它。
    /// </summary>
    /// <returns>
    /// 真写下去了没有 —— 写完再读一遍，注册表被组策略挡住、被安全软件拦下、或者干脆是只读的时候这里答 false，
    /// 调用方据此把屏上那个开关拨回去并说一句，而不是让开关停在一个没生效的位置上。
    /// </returns>
    bool Apply(bool enabled);
}

/// <inheritdoc cref="IStartupRegistration"/>
internal sealed class StartupRegistration : IStartupRegistration
{
    /// <summary>程序自己那一条住哪儿。值名见 <see cref="StartupEntry.ValueName"/>。</summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// 任务管理器「启动」页把某一条关掉时，关的是这里 —— 同名的一份二进制标记，Run 那一格原封不动。
    /// 所以「关掉了」这件事只看上面那一格是看不出来的，见 <see cref="StartupEntry.TurnedOff"/>。
    /// </summary>
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public bool Available => !Packaged();

    public bool IsEnabled =>
        StartupEntry.IsEnabled(Stored, Approval, ExePath);

    public bool Apply(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                    run?.SetValue(StartupEntry.ValueName, StartupEntry.Command(ExePath), RegistryValueKind.String);

                // 上一次被任务管理器关掉过的话，那一份标记还在，而它会继续压着这一条 —— 清掉它，
                // 否则「拨开」在屏上生效、登录时照样不启动，正是这一类功能最容易骗人的地方。
                using (var approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, writable: true))
                    approval?.DeleteValue(StartupEntry.ValueName, throwOnMissingValue: false);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(StartupEntry.ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn(Category, $"改不动开机自启那一条（{(enabled ? "写" : "删")}）：{error.Message}");
            return false;
        }

        // 写完读回来问一句「现在真的是这样吗」，而不是假定写成功 —— 上面那几种失败之外，还有
        // 「值写进去了但读回来不是这一份」这类环境问题。
        return IsEnabled == enabled;
    }

    /// <summary>Run 那一格里存着的那一行，没有就是 null。</summary>
    private static string? Stored
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(StartupEntry.ValueName) as string;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn(Category, $"读不到开机自启那一条：{error.Message}");
                return null;
            }
        }
    }

    /// <summary>任务管理器留下的那一份标记，没被动过就是 null（也就是开着）。</summary>
    private static byte[]? Approval
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ApprovalKey);
                return key?.GetValue(StartupEntry.ValueName) as byte[];
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn(Category, $"读不到开机自启的启动状态：{error.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 要挂上去的那个可执行文件。<see cref="Environment.ProcessPath"/> 是这份 apphost 自己的路径；它为空只
    /// 在非常规宿主下发生，那时退到程序目录下与程序同名的那一个 —— 也就是发布件里那个 exe。
    /// </summary>
    private static string ExePath =>
        Environment.ProcessPath is { Length: > 0 } path
            ? path
            : Path.Combine(AppContext.BaseDirectory, AppIdentity.Title + ".exe");

    /// <summary>
    /// 这一份进程是不是以 MSIX 包身份跑起来的。问不到就答「不是」—— 猜错的方向是藏掉一个其实办得到的功能，
    /// 比多露一格糟（同设置页核账号权限那一处的取舍）。
    /// </summary>
    private static bool Packaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const string Category = "开机自启";
}
