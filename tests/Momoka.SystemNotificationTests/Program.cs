using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Shell.Platform;

namespace Momoka.Tests;

internal static partial class Program
{
    private const string Executable = "MomokaSystemNotificationProbe.exe";
    private const string Title = "Momoka 系统通知验证";
    private const int ModuleNotFound = unchecked((int)0x8007007E);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is not (["--send-self-contained"] or ["--send-framework-dependent"])
            || !string.Equals(Path.GetFileName(Environment.ProcessPath), Executable, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("请通过独立测试程序显式指定 --send-self-contained 或 --send-framework-dependent。");
            return 2;
        }

        using var desktop = new Mutex(false, SelfCheckRun.MutexName);
        try
        {
            if (!desktop.WaitOne(0))
            {
                Console.Error.WriteLine("共享桌面正在被另一项诊断使用。");
                return 3;
            }
        }
        catch (AbandonedMutexException)
        {
            // The abandoned lock now belongs to this thread.
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            return CheckDeliveryAsync(expectSelfContained: args is ["--send-self-contained"]).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"通知检查失败：{error.GetType().Name} (0x{error.HResult:X8})");
            return 1;
        }
        finally
        {
            desktop.ReleaseMutex();
        }
    }

    private static async Task<int> CheckDeliveryAsync(bool expectSelfContained)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll")) != expectSelfContained
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.Insights.Resource.dll")))
            throw new InvalidOperationException("测试产物的 Windows App SDK 形态与指定参数不一致。");
        if (!AppNotificationManager.IsSupported())
        {
            Console.Error.WriteLine("当前进程不支持 Windows 通知。");
            return 3;
        }

        var runId = Guid.NewGuid().ToString("N");
        var appId = "Momoka.SystemNotificationProbe." + runId;
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(appId));
        var message = "匿名测试 " + runId;
        var logs = new RingBufferLogSink();
        Log.UseSink(logs);
        var manager = AppNotificationManager.Default;
        var fullRegistration = false;
        var attempted = false;
        try
        {
            SystemNotification.Register();
            var registrationWarnings = logs.Snapshot();
            fullRegistration = registrationWarnings.Count == 0;
            if (!fullRegistration)
            {
                var warning = registrationWarnings.SingleOrDefault();
                if (!expectSelfContained || registrationWarnings.Count != 1
                    || warning.Detail?.Contains($"{ModuleNotFound:X8}", StringComparison.OrdinalIgnoreCase) != true
                    || !warning.Message.Contains("已保留发送功能", StringComparison.Ordinal))
                    throw new InvalidOperationException("Windows 通知注册失败。");
                Console.WriteLine("自包含通知使用仅发送模式。");
            }

            if (manager.Setting != AppNotificationSetting.Enabled)
            {
                Console.Error.WriteLine($"系统通知不可用，当前状态为 {manager.Setting}。");
                return 3;
            }

            var watermark = logs.SnapshotAfter(0).Watermark;
            attempted = true;
            SystemNotification.Show(Title, message);
            if (logs.SnapshotAfter(watermark).Entries.Count != 0)
                throw new InvalidOperationException("系统通知发送失败。");

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (true)
            {
                var matches = await ReadTestNotificationsAsync(manager, message, deadline.Token).ConfigureAwait(false);
                if (matches.Count > 0)
                {
                    var match = matches.Single();
                    if (match.Id == 0) throw new InvalidOperationException("Windows 未返回有效的通知 ID。");
                    Console.WriteLine($"Windows 通知中心收到唯一匹配的通知，ID={match.Id}。");
                    return 0;
                }
                await Task.Delay(120, deadline.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (attempted)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    foreach (var match in await ReadTestNotificationsAsync(manager, message, cleanup.Token).ConfigureAwait(false))
                        await manager.RemoveByIdAsync(match.Id).AsTask(cleanup.Token).ConfigureAwait(false);
                    var remaining = await ReadTestNotificationsAsync(manager, message, cleanup.Token).ConfigureAwait(false);
                    if (remaining.Count != 0) throw new InvalidOperationException("测试通知没有完全清除。");
                    Console.WriteLine("本次测试通知按 ID 清除，复查剩余 0 条。");
                }
            }
            finally
            {
                var watermark = logs.SnapshotAfter(0).Watermark;
                SystemNotification.Unregister();
                if (fullRegistration) manager.UnregisterAll();
                CleanAnonymousIdentity(appId);
                if (logs.SnapshotAfter(watermark).Entries.Count != 0)
                    throw new InvalidOperationException("撤销通知注册失败。");
            }
        }
    }

    private static async Task<IReadOnlyList<AppNotification>> ReadTestNotificationsAsync(AppNotificationManager manager,
        string message, CancellationToken token)
    {
        var notifications = await manager.GetAllAsync().AsTask(token).ConfigureAwait(false);
        return notifications.Where(notification => XElement.Parse(notification.Payload)
            .Descendants("text").Select(element => element.Value)
            .SequenceEqual([Title, message], StringComparer.Ordinal)).ToArray();
    }

    private static void CleanAnonymousIdentity(string appId)
    {
        // The affected SDK can fail its resource check before COM registration and UnregisterAll.
        // Only the per-run app ID and a COM server pointing to this probe qualify for cleanup.
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true)
            ?? throw new InvalidOperationException("无法打开本次测试的通知注册表位置。");
        var identityPath = $@"AppUserModelId\{appId}";
        string? activationId;
        using (var identity = classes.OpenSubKey(identityPath))
        {
            if (identity is null) return;
            activationId = identity.GetValue("CustomActivator") as string;
        }

        if (Guid.TryParse(activationId, out var clsid))
        {
            var classPath = $@"CLSID\{clsid:B}";
            string? command;
            using (var server = classes.OpenSubKey($@"{classPath}\LocalServer32"))
                command = server?.GetValue("") as string;
            var expected = $"\"{Environment.ProcessPath}\" ----AppNotificationActivated:";
            if (!string.Equals(command, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试通知的 COM 激活记录与当前程序不符。");
            classes.DeleteSubKeyTree(classPath);
        }
        classes.DeleteSubKeyTree(identityPath);
        using var remaining = classes.OpenSubKey(identityPath);
        if (remaining is not null) throw new InvalidOperationException("测试通知身份没有清除。");
        Console.WriteLine("本次测试的通知身份已清除。");
    }
}
