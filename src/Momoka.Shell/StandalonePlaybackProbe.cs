using System.Diagnostics;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Interop;
using static Momoka.Infrastructure.StartupArgs;

namespace Momoka.Shell;

/// <summary>Tests the real standalone native window/uosc lifecycle using only local media fixtures.</summary>
internal static class StandalonePlaybackProbe
{
    internal static int ExitCode { get; private set; } = 1;
    private const int WmClose = 0x0010;
    private const uint SmtoAbortIfHung = 0x0002;
    private const string RedFixtureHash = "BFF2E52BB5A21C3C055B234C8EC2BFC0883C40F12F4C36EAEC8F115295078B94";
    private const string CyanFixtureHash = "14E64ADB6F501BEA147B0C50099851495793E44D2675CDD25FBA194A8313AA49";

    internal static bool IsRequested(string[] args) => args.Any(argument =>
        argument.TrimStart('-', '/').Split('=', 2)[0]
            .Equals("probe-standalone-playback", StringComparison.OrdinalIgnoreCase));

    internal static (string FirstFile, string SecondFile) ValidateFixtures(string[] args)
    {
        var files = ValidateStandalonePlaybackProbe(args);
        var first = ValidateFixture(files.FirstFile, "standalone-red.mp4", RedFixtureHash);
        var second = ValidateFixture(files.SecondFile, "standalone-cyan.mp4", CyanFixtureHash);
        return (first, second);
    }

    private static string ValidateFixture(string candidate, string expectedName, string expectedHash)
    {
        var path = CompositionPlaybackProbe.LocalFile(candidate).LocalPath;
        if (!string.Equals(Path.GetFileName(path), expectedName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"探针只接受随项目提供的 {expectedName} 测试素材");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        if (!hash.Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException($"{expectedName} 素材校验失败");
        return path;
    }

    internal static async Task RunAsync(StartupOptions options)
    {
        ExitCode = 1;
        var passed = false;
        StreamWriter? report = null;
        LibMpvHandle? handle = null;
        var ownedWindows = new HashSet<IntPtr>();
        var tag = "Momoka-Standalone-" + Guid.NewGuid().ToString("N");
        try
        {
            var first = ValidateFixture(options.ProbeStandaloneFile!, "standalone-red.mp4", RedFixtureHash);
            var second = ValidateFixture(options.ProbeStandaloneNextFile!, "standalone-cyan.mp4", CyanFixtureHash);
            Directory.CreateDirectory(options.Paths.LogDirectory);
            report = new StreamWriter(Path.Combine(options.Paths.LogDirectory, "standalone-playback-probe.txt")) { AutoFlush = true };
            Write($"独占本地探针 PID={Environment.ProcessId}；素材：{Path.GetFileName(first)}、{Path.GetFileName(second)}。");
            Write("无 ShellServices、EmbySession、设置读取或服务器上报；窗口只按本轮唯一标题和当前 PID 识别。");

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(75));
            var token = deadline.Token;
            var settings = new AppSettings();
            settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
            settings.Mpv.Pipeline = VideoPipelineKind.Standalone;
            settings.Mpv.SvpEnabled = false;
            settings.Mpv.CustomConfigEnabled = false;
            settings.Video.GpuApi = "d3d11";
            settings.Playback.ReportProgressToServer = false;
            settings.Playback.ResumeFromSavedPosition = false;
            settings.Playback.PinWindowTopmost = false;
            var playerOptions = MpvBaseline.Build(
                    MpvBaseline.ScreenshotOptions.From(settings.Mpv),
                    screenshotDirectory: options.Paths.ScreenshotDirectory)
                .Concat(MpvOutputOptions.Build(settings.Video, settings.Audio, settings.Playback, gpuNext: true))
                .Append(new KeyValuePair<string, string>("mute", "yes"))
                .Append(new KeyValuePair<string, string>("loop-file", "inf"))
                .Append(new KeyValuePair<string, string>("access-references", "no"))
                .Append(new KeyValuePair<string, string>("load-unsafe-playlists", "no"))
                .Append(new KeyValuePair<string, string>("ao", "null"))
                .Append(new KeyValuePair<string, string>("title", tag))
                .Append(new KeyValuePair<string, string>("demuxer-lavf-o", "protocol_whitelist=file"))
                .ToArray();
            var backend = new LibMpvBackend(settings.Mpv, settings.Playback, () => null);
            var firstRequest = Request(first, tag, playerOptions);
            var nextRequest = Request(second, tag, playerOptions);

            var existing = VisibleOwnedWindows();
            handle = await backend.StartAsync(firstRequest, token) as LibMpvHandle
                ?? throw new InvalidOperationException("未建立正式内置 libmpv 会话");
            Require(handle.PictureInHostWindow == false, "画面没有进入 WinUI 宿主窗");
            await UntilAsync(() => handle.VideoWindowUiReady, token, "uosc 已发送真实就绪握手");
            await UntilAsync(() => handle.Status.Loaded, token, "首个文件进入 loaded");
            var window = await FindWindowAsync(existing, tag, token);
            ownedWindows.Add(window);
            Write($"原生窗口识别：{DescribeWindow(window)}");
            await RequirePlaybackAdvanceAsync(handle, token);
            await RequireRenderedColorAsync(window, "首次起播", red: true, token);

            var generation = handle.FileGeneration;
            Require(handle.CanSwapTo(nextRequest), "正式 libmpv 接受第二份配置相同的换片请求");
            var firstExit = handle.WaitForExitAsync(CancellationToken.None);
            handle.HandOver();
            Require(await handle.SwapToAsync(nextRequest, token), "正式 libmpv 接受原生 loadfile replace 换片");
            await UntilFilenameAsync(handle, Path.GetFileName(second), generation, token);
            Require((await firstExit.WaitAsync(token)).Reason == PlaybackEndReason.Stopped,
                "原文件生命周期在交接时结束");
            Require(handle.VideoWindowUiReady, "同窗换片后 uosc 仍就绪");
            Require(IsVisibleOwnedWindow(window), "同窗换片保留原播放器窗口");
            await RequirePlaybackAdvanceAsync(handle, token);
            await RequireRenderedColorAsync(window, "同窗换片", red: false, token);
            await CloseWindowAsync(handle, window, token);
            await handle.DisposeAsync();
            await handle.NativeShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(8), token);
            await UntilAsync(() => !IsVisibleOwnedWindow(window), token,
                "原生销毁完成后窗口句柄消失");
            handle = null;
            ownedWindows.Remove(window);
            Write("通过：窗口关闭后后台原生资源回收任务也已完成。");

            existing = VisibleOwnedWindows();
            var restartTag = tag + "-restart";
            var restartOptions = playerOptions.Select(option => option.Key == "title"
                ? new KeyValuePair<string, string>(option.Key, restartTag) : option).ToArray();
            handle = await backend.StartAsync(Request(first, restartTag, restartOptions), token) as LibMpvHandle
                ?? throw new InvalidOperationException("原生实例销毁后未能重新创建播放器");
            Require(handle.PictureInHostWindow == false, "重启后仍使用独占管线");
            await UntilAsync(() => handle.VideoWindowUiReady, token, "重启后 uosc 再次握手");
            await UntilAsync(() => handle.Status.Loaded, token, "重启后本地文件重新进入 loaded");
            var restartedWindow = await FindWindowAsync(existing, restartTag, token);
            ownedWindows.Add(restartedWindow);
            Write($"重启原生窗口识别：{DescribeWindow(restartedWindow)}");
            await RequirePlaybackAdvanceAsync(handle, token);
            await RequireRenderedColorAsync(restartedWindow, "销毁后再次起播", red: true, token);
            await CloseWindowAsync(handle, restartedWindow, token);
            await handle.DisposeAsync();
            await handle.NativeShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(8), token);
            await UntilAsync(() => !IsVisibleOwnedWindow(restartedWindow), token,
                "重启实例销毁完成后窗口句柄消失");
            handle = null;
            ownedWindows.Remove(restartedWindow);
            passed = true;
            Write("结果：关闭窗口、原生销毁任务完成、重启和再起播均通过；真实屏幕色彩有变化。");
            Write("范围不包括真实鼠标菜单操作、播放页集成、外置 mpv 或 Emby 上报。");
        }
        catch (Exception error)
        {
            passed = false;
            Write($"失败：{error.GetType().Name}：{error.Message}");
            Log.Error("独占播放本地探针", "验证失败", error);
        }
        finally
        {
            try
            {
                if (handle is not null)
                {
                    try
                    {
                        var window = FindOwnedWindowByProcessTag();
                        if (window != IntPtr.Zero) SendClose(window);
                    }
                    catch (Exception error)
                    {
                        passed = false;
                        Log.Error("独占播放本地探针", "未能安全关闭播放器窗口", error);
                    }
                    try { await handle.StopAsync().WaitAsync(TimeSpan.FromSeconds(4)); }
                    catch (Exception error)
                    {
                        passed = false;
                        Log.Error("独占播放本地探针", "播放器停止请求失败", error);
                    }
                    try { await handle.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception error)
                    {
                        passed = false;
                        Log.Error("独占播放本地探针", "播放器释放请求失败", error);
                    }
                    try { await handle.NativeShutdownCompletion.WaitAsync(TimeSpan.FromSeconds(8)); }
                    catch (Exception error)
                    {
                        passed = false;
                        Log.Error("独占播放本地探针", "原生资源回收任务未能完成", error);
                    }
                }
                var watch = Stopwatch.StartNew();
                while (ownedWindows.Any(IsVisibleOwnedWindow) && watch.Elapsed < TimeSpan.FromSeconds(4))
                    await Task.Delay(100);
                if (ownedWindows.Any(IsVisibleOwnedWindow)) passed = false;
                Write(passed ? "收尾：所有本轮播放器窗口已消失。" : "收尾：探针失败或有窗口未退出。");
            }
            catch (Exception error)
            {
                passed = false;
                Log.Error("独占播放本地探针", "播放器收尾失败", error);
            }
            finally
            {
                try { report?.Dispose(); }
                catch (Exception error)
                {
                    passed = false;
                    Log.Error("独占播放本地探针", "验证报告写入失败", error);
                }
                ExitCode = passed ? 0 : 1;
                try { Application.Current.Exit(); }
                catch (Exception error) { Log.Error("独占播放本地探针", "应用退出请求失败", error); }
            }
        }

        void Write(string message)
        {
            try { report?.WriteLine($"{DateTimeOffset.Now:O} {message}"); }
            catch (Exception error)
            {
                passed = false;
                Log.Error("独占播放本地探针", "验证报告写入失败", error);
            }
            Log.Info("独占播放本地探针", message);
        }
        void Require(bool value, string description)
        {
            Write($"{(value ? "通过" : "失败")}：{description}");
            if (!value) throw new InvalidOperationException(description);
        }
    }

    private static PlaybackRequest Request(string path, string title, IReadOnlyList<KeyValuePair<string, string>> options) => new()
    {
        MediaUrl = new Uri(path, UriKind.Absolute),
        Title = title,
        SubtitlesDisabled = true,
        PlayerOptions = options
    };

    private static async Task CloseWindowAsync(LibMpvHandle handle, IntPtr window, CancellationToken token)
    {
        if (!IsVisibleOwnedWindow(window)) throw new InvalidOperationException("目标播放器窗失效或不属于本进程");
        SendClose(window);
        var exit = await handle.WaitForExitAsync(token);
        if (exit.Reason != PlaybackEndReason.UserQuit)
            throw new InvalidOperationException($"WM_CLOSE 应产生 UserQuit，实际为 {exit.Reason}");
    }

    private static void SendClose(IntPtr window)
    {
        if (!IsVisibleOwnedWindow(window)) return;
        var sent = Native.SendMessageTimeout(window, WmClose, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 1000, out var result);
        if (sent == IntPtr.Zero)
            throw new TimeoutException("原生播放器未在一秒内接受 WM_CLOSE");
        WriteEvidence($"WM_CLOSE 已分发，目标窗口返回值=0x{result:X}");
    }

    private static async Task RequireRenderedColorAsync(IntPtr window, string stage, bool red, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(7))
        {
            await Task.Delay(120, token);
            var current = ReadCenterColor(window);
            var redChannel = current & 0xFF;
            var greenChannel = (current >> 8) & 0xFF;
            var blueChannel = (current >> 16) & 0xFF;
            var matches = red
                ? redChannel > 175 && greenChannel < 105 && blueChannel < 105
                : redChannel < 105 && greenChannel > 145 && blueChannel > 145;
            if (matches)
            {
                WriteEvidence($"通过：{stage}实际屏幕中央 RGB #{current:X6}；色块与素材预期一致。");
                return;
            }
        }
        throw new TimeoutException($"{stage}的实际屏幕中央色块未匹配素材内容");
    }

    private static async Task RequirePlaybackAdvanceAsync(LibMpvHandle handle, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        double start = double.NaN;
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            token.ThrowIfCancellationRequested();
            var ready = await handle.GetPositionAsync(token);
            if (ready is { } position && double.IsFinite(position) && position >= 0)
            {
                start = position;
                break;
            }
            await Task.Delay(80, token);
        }
        if (!double.IsFinite(start) || start < 0) throw new TimeoutException("本地文件没有产生首个有效播放位置");
        watch.Restart();
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            var current = await handle.GetPositionAsync(token);
            if (current is { } position && double.IsFinite(position) && position >= start + 0.35)
            {
                WriteEvidence("通过：本地文件播放位置持续推进");
                return;
            }
            await Task.Delay(100, token);
        }
        throw new TimeoutException("本地文件播放位置没有持续推进");
    }

    private static uint ReadCenterColor(IntPtr window)
    {
        if (!IsVisibleOwnedWindow(window) || !Native.GetClientRect(window, out var client)
            || client.Width < 32 || client.Height < 32)
            throw new InvalidOperationException("目标原生播放器窗口已隐藏或尺寸无效");
        var point = new NativePoint { X = client.Width / 2, Y = client.Height / 2 };
        if (!Native.ClientToScreen(window, ref point)) throw new InvalidOperationException("无法定位原生视频画面");
        var hit = Native.WindowFromPoint(point);
        var hitRoot = Native.GetAncestor(hit, Native.GaRoot);
        if (hitRoot != window)
        {
            Native.GetWindowThreadProcessId(hit, out var hitPid);
            var hitClassBuffer = new char[128];
            var hitClassLength = hit == IntPtr.Zero ? 0 : Native.GetClassName(hit, hitClassBuffer, hitClassBuffer.Length);
            Native.GetWindowThreadProcessId(window, out var targetPid);
            var targetClassBuffer = new char[128];
            var targetClassLength = Native.GetClassName(window, targetClassBuffer, targetClassBuffer.Length);
            throw new InvalidOperationException($"原生播放器窗口被遮挡；target=0x{window:X}/pid={targetPid}/class={new string(targetClassBuffer, 0, Math.Max(0, targetClassLength))}; hit=0x{hit:X}/root=0x{hitRoot:X}/pid={hitPid}/class={new string(hitClassBuffer, 0, Math.Max(0, hitClassLength))}");
        }
        var screen = Native.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("无法读取屏幕像素");
        try
        {
            var color = unchecked((uint)Native.GetPixel(screen, point.X, point.Y));
            if (color == uint.MaxValue) throw new InvalidOperationException("GetPixel 未返回真实画面颜色");
            return color & 0xFFFFFF;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }

    private static IReadOnlySet<IntPtr> VisibleOwnedWindows()
    {
        var result = new HashSet<IntPtr>();
        var window = IntPtr.Zero;
        for (var count = 0; count < 512; count++)
        {
            window = Native.NextWindow(IntPtr.Zero, window, null, null);
            if (window == IntPtr.Zero) break;
            if (IsVisibleOwnedWindow(window)) result.Add(window);
        }
        return result;
    }

    private static async Task<IntPtr> FindWindowAsync(IReadOnlySet<IntPtr> before, string title, CancellationToken token)
    {
        IntPtr found = IntPtr.Zero;
        await UntilAsync(() =>
        {
            var candidates = VisibleOwnedWindows().Where(window => !before.Contains(window)
                && ReadTitle(window).Contains(title, StringComparison.Ordinal)).ToArray();
            if (candidates.Length > 1) throw new InvalidOperationException("本轮媒体标题匹配到多扇玩家窗口");
            if (candidates.Length == 1) found = candidates[0];
            return found != IntPtr.Zero;
        }, token, $"创建当前唯一标题的可见原生窗口：{title}");
        return found;
    }

    private static IntPtr FindOwnedWindowByProcessTag() =>
        VisibleOwnedWindows().FirstOrDefault(window => ReadTitle(window).Contains("Momoka-Standalone-", StringComparison.Ordinal));

    private static string DescribeWindow(IntPtr window)
    {
        var name = new char[128];
        var length = Native.GetClassName(window, name, name.Length);
        Native.GetWindowThreadProcessId(window, out var process);
        return $"HWND=0x{window:X},PID={process},class={new string(name, 0, Math.Max(0, length))},title={ReadTitle(window)}";
    }

    private static async Task UntilFilenameAsync(LibMpvHandle handle, string expected, long previousGeneration,
        CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(12))
        {
            token.ThrowIfCancellationRequested();
            var current = await handle.GetTextAsync("filename", token);
            if (handle.FileGeneration > previousGeneration
                && string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            {
                WriteEvidence("通过：第二个素材在同一原生实例加载，文件代际增加");
                return;
            }
            await Task.Delay(80, token);
        }
        throw new TimeoutException("第二个素材没有在同窗切换后加载");
    }

    private static string ReadTitle(IntPtr window)
    {
        var text = new char[256];
        var length = Native.GetWindowText(window, text, text.Length);
        return length > 0 ? new string(text, 0, length) : "";
    }

    private static bool IsVisibleOwnedWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !Native.IsWindowVisible(window)) return false;
        Native.GetWindowThreadProcessId(window, out var process);
        return process == (uint)Environment.ProcessId;
    }

    private static async Task UntilAsync(Func<bool> condition, CancellationToken token, string description)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            token.ThrowIfCancellationRequested();
            if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException(description);
            await Task.Delay(80, token);
        }
        token.ThrowIfCancellationRequested();
        WriteEvidence("通过：" + description);
    }

    private static void WriteEvidence(string line) => Log.Info("独占播放本地探针", line);
}
