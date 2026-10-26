using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>
/// A separate local console host. It never creates App/Shell, initializes a video/audio output,
/// reads settings/accounts, executes scripts, loads a media file or supplies a URL to mpv.
/// Checks the bundled filter registry/help and option/list readback, not actual frame processing.
/// </summary>
internal static class NativeNoMediaProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetOption(IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetProperty(IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetString(IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LogMessages(IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr WaitEvent(IntPtr context, double timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Command(IntPtr context, IntPtr arguments);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string path);

    [StructLayout(LayoutKind.Sequential)]
    private struct Event { internal int Id; internal int Error; internal ulong UserData; internal IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Log { internal IntPtr Prefix; internal IntPtr Level; internal IntPtr Text; internal int Severity; }

    internal static int Run(string dll)
    {
        try
        {
            if (!Path.IsPathFullyQualified(dll) || dll.StartsWith(@"\\", StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(dll), "libmpv-2.dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("探针只接受本机 libmpv-2.dll 的绝对路径");
            for (var current = new FileInfo(dll) as FileSystemInfo; current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
                if (current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("探针拒绝重解析路径");
            if (new DriveInfo(Path.GetPathRoot(dll)!).DriveType == DriveType.Network)
                throw new ArgumentException("探针拒绝网络盘");
            if (!SetDefaultDllDirectories(0x00001000)) throw new InvalidOperationException("无法设置本地 DLL 搜索边界");
            var sibling = AddDllDirectory(Path.GetDirectoryName(dll)!);
            var root = FindRoot();
            var runtime = AddDllDirectory(Path.Combine(root, "assets", "mpv-runtime"));
            if (sibling == IntPtr.Zero || runtime == IntPtr.Zero) throw new InvalidOperationException("无法注册本地 DLL 依赖目录");
            var module = NativeLibrary.Load(dll);
            // Deliberately keep the native module loaded until this independent process exits.
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
            var create = Bind<Create>("mpv_create");
            var initialize = Bind<Initialize>("mpv_initialize");
            var destroy = Bind<Destroy>("mpv_terminate_destroy");
            var set = Bind<SetOption>("mpv_set_option_string");
            var property = Bind<SetProperty>("mpv_set_property_string");
            var get = Bind<GetString>("mpv_get_property_string");
            var free = Bind<Free>("mpv_free");
            var requestLogs = Bind<LogMessages>("mpv_request_log_messages");
            var wait = Bind<WaitEvent>("mpv_wait_event");
            var command = Bind<Command>("mpv_command");
            int FilterHelp(IntPtr context)
            {
                // The command path supplies an mp_log; mpv_set_option_string's node path does not
                // print per-filter help. A help-only vf command does not load or produce any frames.
                var values = new[] { "vf", "set", "minterpolate=help" }.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
                var arguments = Marshal.AllocHGlobal((values.Length + 1) * IntPtr.Size);
                try
                {
                    for (var index = 0; index < values.Length; index++) Marshal.WriteIntPtr(arguments, index * IntPtr.Size, values[index]);
                    Marshal.WriteIntPtr(arguments, values.Length * IntPtr.Size, IntPtr.Zero);
                    return command(context, arguments);
                }
                finally
                {
                    foreach (var value in values) Marshal.FreeCoTaskMem(value);
                    Marshal.FreeHGlobal(arguments);
                }
            }
            var checks = 0;
            void Check(bool ok, string label)
            {
                if (!ok) throw new InvalidOperationException(label);
                checks++;
                Console.WriteLine("通过 " + label);
            }
            string? Read(IntPtr context, string name)
            {
                var value = get(context, name);
                if (value == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUTF8(value); }
                finally { free(value); }
            }
            IntPtr Context()
            {
                var context = create();
                if (context == IntPtr.Zero) throw new InvalidOperationException("mpv_create 失败");
                try
                {
                    // No App startup/DI or account IO. No media/URLs and no scripts/input/config discovery.
                    foreach (var (name, value) in new[]
                    {
                        ("config", "no"), ("load-scripts", "no"), ("scripts", ""), ("autoload-files", "no"),
                        ("ytdl", "no"), ("resume-playback", "no"), ("save-position-on-quit", "no"),
                        ("stream-lavf-o", "protocol_whitelist=file"), ("demuxer-lavf-o", "protocol_whitelist=file"),
                        ("input-default-bindings", "no"), ("input-terminal", "no"), ("input-ipc-server", ""),
                        ("terminal", "no"), ("idle", "yes"), ("video", "no"), ("audio", "no"),
                        ("vo", "null"), ("ao", "null"), ("osc", "no"), ("force-window", "no")
                    })
                    {
                        var code = set(context, name, value);
                        if (code < 0) throw new InvalidOperationException($"隔离选项 {name} 被拒绝：{code}");
                    }
                    if (requestLogs(context, "info") < 0) throw new InvalidOperationException("无法订阅隔离日志");
                    return context;
                }
                catch { destroy(context); throw; }
            }
            string Drain(IntPtr context)
            {
                var text = new StringBuilder();
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 1000)
                {
                    var pointer = wait(context, 0.02);
                    if (pointer == IntPtr.Zero) continue;
                    var entry = Marshal.PtrToStructure<Event>(pointer);
                    if (entry.Id == 0) continue;
                    if (entry.Id != 2 || entry.Data == IntPtr.Zero) continue;
                    var log = Marshal.PtrToStructure<Log>(entry.Data);
                    text.Append(Marshal.PtrToStringUTF8(log.Text));
                }
                return text.ToString();
            }

            var registry = Context();
            try
            {
                Check(initialize(registry) >= 0, "无媒体句柄初始化，音视频输出均关闭");
                Console.WriteLine("内核版本：" + Read(registry, "mpv-version"));
                Console.WriteLine("FFmpeg：" + Read(registry, "ffmpeg-version"));
                var filterNames = Read(registry, "option-info/vf/choices") ?? "";
                // vf=help queries FFmpeg's av_filter_iterate registry, not just mpv's graph parser.
                _ = set(registry, "vf", "help");
                var available = Drain(registry);
                Check(filterNames.Contains("minterpolate", StringComparison.Ordinal)
                    || available.Contains("minterpolate", StringComparison.Ordinal), "本地 libavfilter 注册 minterpolate");
                var helpContext = Context();
                string help;
                try
                {
                    Check(initialize(helpContext) >= 0, "逐项帮助无媒体句柄初始化");
                    _ = Drain(helpContext);
                    var helpCode = FilterHelp(helpContext);
                    help = Drain(helpContext);
                    // Help intentionally exits filter parsing; the log is the asserted registry evidence.
                    Console.WriteLine($"minterpolate help-only 结果：{helpCode}，日志字符数 {help.Length}");
                }
                finally { destroy(helpContext); }
                foreach (var option in new[] { "fps", "mi_mode", "mc_mode", "me_mode", "vsbmc", "mci", "obmc", "aobmc", "bidir", "bilat" })
                    Check(help.Contains(option, StringComparison.Ordinal), "本地 minterpolate 支持 " + option);
                foreach (var name in new[] { "fps", "mi_mode", "mc_mode", "me_mode", "vsbmc" })
                {
                    var line = help.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith(name + " ", StringComparison.Ordinal));
                    if (line is not null) Console.WriteLine("滤镜参数：" + line.Trim());
                }
                foreach (var hwdec in new[] { "no", "auto-copy-safe", "d3d11va-copy", "d3d12va-copy", "dxva2-copy", "nvdec-copy" })
                    Check(set(registry, "hwdec", hwdec) >= 0, "本地内核接受复制／软件解码候选 " + hwdec);
                Console.WriteLine("vf-append 原生接口结果：" + set(registry, "vf-append", "hflip")
                    + "（产品使用完整 vf 合并，不依赖此接口）");
            }
            finally { destroy(registry); }

            foreach (var quality in Enum.GetValues<FrameInterpolationQuality>())
            {
                var context = Context();
                try
                {
                    var video = new VideoSettings
                    {
                        FrameInterpolationMode = FrameInterpolationMode.MotionCompensated,
                        FrameInterpolationQuality = quality,
                        HardwareDecoding = "d3d11va",
                        DolbyVisionMetadata = false
                    };
                    var options = MpvOutputOptions.Build(video, new AudioSettings(), source: new(1920, 1080, 8, 24, false));
                    foreach (var pair in options)
                        Check(set(context, pair.Key, pair.Value) >= 0, $"{quality} 起播选项 {pair.Key} 被本地内核接受");
                    // The vo candidate above is only acceptance-tested. Pin the probe back to null
                    // before initialization so even its configured output cannot create a window.
                    Check(set(context, "vo", "null") >= 0, quality + " 探针初始化前固定回空输出");
                    Check(initialize(context) >= 0, quality + " 无媒体初始化");
                    var filters = Read(context, "vf") ?? "";
                    Check(filters.Contains("momoka-hdr", StringComparison.Ordinal)
                        && filters.Contains("minterpolate", StringComparison.Ordinal), quality + " vf 保留 HDR 格式和补帧两条标签");
                    Check(Read(context, "hwdec") == "d3d11va-copy", quality + " 有效硬解为复制模式");
                    Check(Read(context, "interpolation") == "no", quality + " 不叠加相邻帧平滑");
                    Check(Read(context, "video-sync") == "audio", quality + " 有效音频同步");
                    Check(Read(context, "current-vo") is null && Read(context, "current-ao") is null, quality + " 未启动音视频输出");
                    // Live complete-list replacement proves empty vf is accepted; no loadfile is ever sent.
                    Check(property(context, "vf", "@momoka-hdr:format=dolbyvision=no") >= 0, quality + " 关闭后保留 HDR format");
                    Check(!(Read(context, "vf") ?? "").Contains("minterpolate", StringComparison.Ordinal), quality + " 关闭后无运动补偿残留");
                    Check(property(context, "vf", "") >= 0, quality + " 完整 vf 清空复位");
                    var names = new[] { "vf", "hwdec", "interpolation", "video-sync" };
                    var defaults = names.ToDictionary(name => name,
                        name => Read(context, "option-info/" + name + "/default-value") ?? "", StringComparer.Ordinal);
                    video.HighFrameRateAudioSync = false;
                    foreach (var mode in new[] { FrameInterpolationMode.Off, FrameInterpolationMode.Smooth, FrameInterpolationMode.MotionCompensated })
                    {
                        video.FrameInterpolationMode = mode;
                        var next = new PlaybackRequest
                        {
                            MediaUrl = new Uri("file:///C:/no-media-is-loaded.mkv"),
                            Title = "仅规则复位，不加载媒体",
                            PlayerOptions = MpvOutputOptions.Build(video, new AudioSettings(), source: new(1920, 1080, 8, 24, false))
                        };
                        foreach (var pair in InlineSwitch.FilmScoped(defaults, next).Where(pair => names.Contains(pair.Key)))
                            Check(property(context, pair.Key, pair.Value) >= 0, $"{quality}/{mode} 真实属性复位 {pair.Key}");
                        var value = Read(context, "vf") ?? "";
                        Check(value.Contains("momoka-hdr", StringComparison.Ordinal), $"{quality}/{mode} 复位仍保留格式滤镜");
                        Check(value.Contains("minterpolate", StringComparison.Ordinal) == (mode == FrameInterpolationMode.MotionCompensated),
                            $"{quality}/{mode} 完整复位的运动补偿状态正确");
                        Check(Read(context, "hwdec") == (mode == FrameInterpolationMode.MotionCompensated ? "d3d11va-copy" : "d3d11va"),
                            $"{quality}/{mode} 硬解恢复正确");
                        Check(Read(context, "interpolation") == (mode == FrameInterpolationMode.Smooth ? "yes" : "no"),
                            $"{quality}/{mode} 平滑插值状态正确");
                        Check(Read(context, "video-sync") == (mode == FrameInterpolationMode.Smooth ? "display-resample" : "audio"),
                            $"{quality}/{mode} 同步恢复正确");
                        Check(Read(context, "path") is null && Read(context, "current-vo") is null && Read(context, "current-ao") is null,
                            $"{quality}/{mode} 无文件／音视频输出");
                    }
                }
                finally { destroy(context); }
            }
            Console.WriteLine("DLL SHA256：" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
            Console.WriteLine($"原生无媒体探针：{checks} 个断言通过；未加载媒体，未验证滤镜帧输出、实时性能或真实 HDR。");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("原生无媒体探针失败：" + error.Message);
            return 1;
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "src")))
                return directory.FullName;
        throw new InvalidOperationException("探针找不到本树源码根");
    }
}
