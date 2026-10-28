using System.Runtime.InteropServices;
using Momoka.Configuration;

namespace Momoka.Mpv;

/// <summary>Lifetime of the optional DLL search directory and the single SVP pipe owner.</summary>
internal sealed class MpvSvpRuntime : IDisposable
{
    private static readonly NativeModuleBinding Module = new();
    // API 4.1 is the base version requested by the bundled mpv VSScript4 headers.
    internal const int MinimumScriptApi = (4 << 16) | 1;
    private static int _active;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetScriptApi(int version);
    private IntPtr _directory;
    private int _disposed;

    private MpvSvpRuntime() { }

    internal static IDisposable Prepare(string requested)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("SVP 接入仅支持 Windows");
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("另一个 Momoka 播放实例正在使用 SVP，请先停止它");
        var lease = new MpvSvpRuntime();
        try
        {
            var status = MpvSvp.FindLibrary(requested);
            var path = status.Path ?? throw new InvalidOperationException($"SVP 接入未启动：{status.Problem} 也可以关闭 SVP 接入后正常播放。");
            if (Environment.GetEnvironmentVariable("VSSCRIPT_PATH") is { Length: > 0 } configured
                && !string.Equals(TypedPath.Clean(configured), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("VSSCRIPT_PATH 指向另一份 VapourSynth。请使用同一路径，或调整 SVP 的环境设置后重启 Momoka；本次未启动播放。");
            lease._directory = AddDllDirectory(Path.GetDirectoryName(path)!);
            if (lease._directory == IntPtr.Zero) throw new InvalidOperationException("无法注册 SVP 的本地组件目录");
            // Embedded Python/native plugins cannot be safely exchanged or unloaded while a
            // script owns callbacks. Keep the module bound until process exit, just like libmpv.
            // No machine/user environment variables or other player's configuration are changed.
            Module.Select(path, "VapourSynth 已绑定另一份原生库，请重启 Momoka 后切换");
            var module = Module.Resolve(LoadModule);
            var api = Marshal.GetDelegateForFunctionPointer<GetScriptApi>(NativeLibrary.GetExport(module, "getVSScriptAPI"));
            EnsureApi(api.Invoke);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static IntPtr LoadModule(string path)
    {
        // Use the explicitly selected DLL's directory for its dependencies; do not consult CWD.
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00001000);
        if (module == IntPtr.Zero)
            throw new InvalidOperationException("VapourSynth 加载失败。请安装匹配的 64 位 Python／VapourSynth 组件，按 SVP 说明设置环境后重启 Momoka；或关闭 SVP 接入。");
        if (NativeLibrary.TryGetExport(module, "getVSScriptAPI", out _)) return module;
        NativeLibrary.Free(module);
        throw new InvalidOperationException("VSScript.dll 缺少 mpv 所需的 API；请更新 SVP 的 64 位 VapourSynth 组件");
    }

    // An exported symbol only establishes ABI shape. Asking for the API also initializes
    // VSScript/Python and refuses an incompatible or incomplete runtime before loading media.
    // Retain the module on failure as well: embedded Python callbacks can outlive initialization.
    internal static void EnsureApi(Func<int, IntPtr> getApi)
    {
        if (getApi(MinimumScriptApi) == IntPtr.Zero)
            throw new InvalidOperationException("VapourSynth／Python 初始化失败或 API 版本不兼容。请修复 SVP 的 64 位组件与 Python 环境后重启 Momoka，或关闭 SVP 接入。");
    }

    internal static async Task VerifyPipeAsync(CancellationToken cancellationToken, string pipeName = MpvSvp.PipeName)
    {
        // The option parser accepting mpvpipe says nothing about a successful bind. Verify that
        // the current process owns the live pipe before loading media. This verifier sends no commands. Never
        // fall back to another player's pipe or change the standard endpoint behind the UI.
        try
        {
            await using var client = await MpvIpcClient.ConnectAsync(pipeName, Environment.ProcessId,
                TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (client is null) throw new InvalidOperationException("SVP 通道未能建立，未启动播放。请关闭占用 mpvpipe 的其他播放器后重试，或关闭 SVP 接入。");
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException("SVP 通道未能验证，未启动播放。可能有其他播放器占用 mpvpipe；请关闭它后重试，或关闭 SVP 接入。", error);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_directory != IntPtr.Zero) RemoveDllDirectory(_directory);
        _directory = IntPtr.Zero;
        Interlocked.Exchange(ref _active, 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string directory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDllDirectory(IntPtr cookie);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
}
