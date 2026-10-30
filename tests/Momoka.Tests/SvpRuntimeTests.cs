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
        TestHarness.Test("SVP 通道夹具：收到一个字节不能回报零命令", () => PipeChildReceivesByteAsync().GetAwaiter().GetResult());
        TestHarness.Test("SVP 通道夹具：真实管道取消或本地关闭不能回报零命令", () => PipeReadErrorsAsync().GetAwaiter().GetResult());
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

    private static Process StartPipeChild(string name)
    {
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
        return Process.Start(start) ?? throw new InvalidOperationException("无法启动匿名管道夹具");
    }

    private static async Task ForeignPipeAsync()
    {
        var name = "anonymous-foreign-svp-" + Guid.NewGuid().ToString("N");
        using var child = StartPipeChild(name);
        var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync(deadline.Token));
            var error = Assert.Catch<InvalidOperationException>(() => MpvSvpRuntime.VerifyPipeAsync(deadline.Token, name).GetAwaiter().GetResult());
            Assert.Contains("未启动播放", error.Message);
            Assert.Contains("进程身份不符", error.InnerException?.Message);
            var result = await child.StandardOutput.ReadLineAsync(deadline.Token);
            await child.WaitForExitAsync(deadline.Token);
            var output = await stderr;
            var diagnostic = $"child-exit={child.ExitCode}; stderr={output}";
            Assert.Equal("zero-commands", result, diagnostic);
            Assert.Equal(0, child.ExitCode, diagnostic);
            Assert.Equal(string.Empty, output, diagnostic);
        }
        finally
        {
            // Only this synthetic test process is owned here; never terminate a user player.
            if (!child.HasExited) child.Kill();
        }
    }

    private static async Task PipeChildReceivesByteAsync()
    {
        var name = "anonymous-svp-byte-" + Guid.NewGuid().ToString("N");
        using var child = StartPipeChild(name);
        var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync(deadline.Token));
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, System.Security.Principal.TokenImpersonationLevel.Anonymous);
            await client.ConnectAsync(deadline.Token);
            // Keep the writer open until the child reports the byte, so disconnect cannot hide it.
            await client.WriteAsync(new byte[] { 0 }, deadline.Token);
            var result = await child.StandardOutput.ReadLineAsync(deadline.Token);
            await child.WaitForExitAsync(deadline.Token);
            var output = await stderr;
            var diagnostic = $"child-exit={child.ExitCode}; stderr={output}";
            Assert.Null(result, diagnostic);
            Assert.Equal(1, child.ExitCode, diagnostic);
            Assert.Contains(nameof(AssertionException), output);
            Assert.Contains("bytes-read=1", output);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }

    private static async Task PipeReadErrorsAsync()
    {
        var name = "anonymous-svp-read-errors-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connected = pipe.WaitForConnectionAsync(deadline.Token);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, System.Security.Principal.TokenImpersonationLevel.Anonymous);
        await client.ConnectAsync(deadline.Token);
        await connected;
        using var canceled = new CancellationTokenSource();
        var reading = AssertZeroCommandsAsync(pipe, canceled.Token);
        Assert.False(reading.IsCompleted, "对端仍连接且未发字节，读取不能提前当作 EOF");
        canceled.Cancel();
        var error = Assert.Catch<OperationCanceledException>(() => reading.GetAwaiter().GetResult());
        Assert.Equal(canceled.Token, error.CancellationToken);
        pipe.Dispose();
        Assert.Throws<ObjectDisposedException>(() => AssertZeroCommandsAsync(pipe, deadline.Token).GetAwaiter().GetResult());
    }

    private static async Task AssertZeroCommandsAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        // Only a successful EOF read proves zero bytes. Cancellation, local disposal and every IO
        // error remain failures; a foreign owner being rejected is not permission to swallow them.
        var bytes = new byte[1];
        var count = await pipe.ReadAsync(bytes, cancellationToken);
        Assert.Equal(0, count, $"bytes-read={count}");
    }

    internal static int RunPipeChild(string name)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            // Arm the accept before ready: an owner-rejecting client may connect and close
            // immediately. Advertising an unarmed listener can fail accept with ERROR_NO_DATA.
            var connected = pipe.WaitForConnectionAsync(deadline.Token);
            Console.WriteLine("ready");
            connected.GetAwaiter().GetResult();
            AssertZeroCommandsAsync(pipe, deadline.Token).GetAwaiter().GetResult();
            Console.WriteLine("zero-commands");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{error.GetType().Name}; HResult=0x{error.HResult:X8}; {error}");
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
