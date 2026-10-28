using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Momoka.Configuration;
using Momoka.Mpv;

namespace Momoka.Tests;

/// <summary>Anonymous local pipe tests. No SVP Manager, accounts, scripts or media are started.</summary>
internal static class SvpRuntimeTests
{
    internal static void Register()
    {
        TestHarness.Test("SVP 运行时：组件准备失败释放独占许可，可修正后重试", () =>
        {
            for (var index = 0; index < 2; index++)
            {
                var error = Assert.Catch<InvalidOperationException>(() => MpvSvpRuntime.Prepare("missing-anonymous-component"));
                Assert.Contains("SVP 接入未启动", error.Message);
                Assert.DoesNotContain("另一个", error.Message);
            }
        });
        TestHarness.Test("SVP 运行时：模块路径切换的提示说明需要重启", () =>
        {
            var binding = new NativeModuleBinding();
            binding.Select(Path.GetFullPath("anonymous-first/VSScript.dll"));
            binding.Resolve(_ => new IntPtr(1));
            var error = Assert.Catch<InvalidOperationException>(() => binding.Select(Path.GetFullPath("anonymous-second/VSScript.dll"), "VapourSynth 需要重启"));
            Assert.Equal("VapourSynth 需要重启", error.Message);
        });
        TestHarness.Test("SVP 运行时：请求 4.1 API，空指针拒绝启动，成功不加载脚本", () =>
        {
            var requested = 0;
            var calls = 0;
            var error = Assert.Catch<InvalidOperationException>(() => MpvSvpRuntime.EnsureApi(version =>
            {
                requested = version;
                calls++;
                return IntPtr.Zero;
            }));
            Assert.Equal((4 << 16) | 1, requested);
            Assert.Equal(1, calls);
            Assert.Contains("Python", error.Message);
            Assert.Contains("重启", error.Message);
            MpvSvpRuntime.EnsureApi(version =>
            {
                Assert.Equal(requested, version);
                calls++;
                return new IntPtr(1);
            });
            Assert.Equal(2, calls);
        });
        TestHarness.Test("SVP 通道：核验自己的匿名管道且发送零命令", () => OwnPipeAsync().GetAwaiter().GetResult());
        TestHarness.Test("SVP 通道：取消前不建立连接，保留取消语义", () =>
        {
            Assert.Throws<OperationCanceledException>(() => MpvSvpRuntime.VerifyPipeAsync(new CancellationToken(true),
                "anonymous-canceled-" + Guid.NewGuid().ToString("N")).GetAwaiter().GetResult());
        });
        TestHarness.Test("SVP 通道：未建立时明确报错，不误报接入成功", () =>
        {
            var error = Assert.Catch<InvalidOperationException>(() => MpvSvpRuntime.VerifyPipeAsync(CancellationToken.None,
                "anonymous-missing-" + Guid.NewGuid().ToString("N")).GetAwaiter().GetResult());
            Assert.Contains("未启动播放", error.Message);
        });
        TestHarness.Test("SVP 通道：其他进程占用被拒绝且收到零命令", () => ForeignPipeAsync().GetAwaiter().GetResult());
    }

    private static async Task OwnPipeAsync()
    {
        var name = "anonymous-svp-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connect = pipe.WaitForConnectionAsync(deadline.Token);
        var verified = MpvSvpRuntime.VerifyPipeAsync(deadline.Token, name);
        await connect;
        await verified;
        var bytes = new byte[1];
        Assert.Equal(0, await pipe.ReadAsync(bytes, deadline.Token));
    }

    private static async Task ForeignPipeAsync()
    {
        var name = "anonymous-foreign-svp-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // dotnet run uses an apphost; allow a dotnet-hosted invocation as well.
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(SvpRuntimeTests).Assembly.Location);
        start.ArgumentList.Add("svp-pipe-child");
        start.ArgumentList.Add(name);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("无法启动匿名管道夹具");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync(deadline.Token));
            var error = Assert.Catch<InvalidOperationException>(() => MpvSvpRuntime.VerifyPipeAsync(deadline.Token, name).GetAwaiter().GetResult());
            Assert.Contains("未启动播放", error.Message);
            Assert.Equal("zero-commands", await child.StandardOutput.ReadLineAsync(deadline.Token));
            await child.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, child.ExitCode);
        }
        finally
        {
            // Only this synthetic test process is owned here; never terminate a user player.
            if (!child.HasExited) child.Kill();
        }
    }

    internal static int RunPipeChild(string name)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Console.WriteLine("ready");
            pipe.WaitForConnectionAsync(deadline.Token).GetAwaiter().GetResult();
            var bytes = new byte[1];
            var count = pipe.ReadAsync(bytes, deadline.Token).AsTask().GetAwaiter().GetResult();
            if (count != 0) return 1;
            Console.WriteLine("zero-commands");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            return 1;
        }
    }

    internal static int RunNative(string dll)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (!Path.IsPathFullyQualified(dll) || dll.StartsWith(@"\\", StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(dll), "libmpv-2.dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("只接受本地 libmpv-2.dll 绝对路径");
            dll = Path.GetFullPath(dll);
            for (FileSystemInfo? current = new FileInfo(dll); current is not null;
                current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("不接受重解析路径");
            if (new DriveInfo(Path.GetPathRoot(dll)!).DriveType == DriveType.Network) throw new ArgumentException("不接受网络盘");
            var root = Path.GetDirectoryName(dll)!;
            LibMpvNative.UseLibrary(dll);
            LibMpvNative.EnsureDependencyDirectories(root, Path.Combine(root, "assets", "mpv-runtime"));
            var name = "anonymous-svp-native-" + Guid.NewGuid().ToString("N");
            var before = LibMpvNative.mpv_create();
            if (before == IntPtr.Zero) throw new InvalidOperationException("mpv_create 失败");
            var checks = 0;
            void Check(bool ok, string note)
            {
                if (!ok) throw new InvalidOperationException(note);
                checks++;
                Console.WriteLine("通过 " + note);
            }
            try
            {
                foreach (var (key, value) in new[] { ("config", "no"), ("load-scripts", "no"), ("scripts", ""),
                    ("video", "no"), ("audio", "no"), ("vo", "null"), ("ao", "null"), ("idle", "yes"),
                    ("terminal", "no"), ("input-default-bindings", "no"), ("input-terminal", "no"),
                    ("osc", "no"), ("force-window", "no"), ("access-references", "no"), ("ytdl", "no") })
                    Check(LibMpvNative.mpv_set_option_string(before, key, value) >= 0, "隔离选项 " + key);
                var options = MpvOutputOptions.Build(new VideoSettings { FrameInterpolationMode = FrameInterpolationMode.MotionCompensated },
                    new AudioSettings(), svp: true);
                foreach (var option in options.Where(option => option.Key is "hwdec" or "video-sync" or "interpolation" or "hr-seek-framedrop"))
                    Check(LibMpvNative.mpv_set_option_string(before, option.Key, option.Value) >= 0, "SVP 有效选项 " + option.Key);
                Check(LibMpvNative.mpv_set_option_string(before, "input-ipc-server", MpvIpcClient.ToPipePath(name)) >= 0, "独立匿名 IPC 选项");
                Check(LibMpvNative.mpv_initialize(before) >= 0, "无媒体内核初始化");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                MpvSvpRuntime.VerifyPipeAsync(timeout.Token, name).GetAwaiter().GetResult();
                Check(true, "实际 libmpv 管道身份验证");
                var enabled = LibMpvNative.mpv_get_property_string(before, "mpv-configuration");
                try
                {
                    Check(enabled != IntPtr.Zero && System.Runtime.InteropServices.Marshal.PtrToStringUTF8(enabled)!.Contains("-Dvapoursynth=enabled", StringComparison.Ordinal),
                        "随包内核编译 VapourSynth 桥接");
                }
                finally { if (enabled != IntPtr.Zero) LibMpvNative.mpv_free(enabled); }
            }
            finally { LibMpvNative.mpv_terminate_destroy(before); }
            var failure = Assert.Catch<InvalidOperationException>(() => MpvSvpRuntime.VerifyPipeAsync(CancellationToken.None, name).GetAwaiter().GetResult());
            Check(failure.Message.Contains("未启动播放", StringComparison.Ordinal), "销毁后匿名管道消失");
            Console.WriteLine($"共 {checks} 项通过；未加载媒体、脚本、SVP 或用户设置。");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
