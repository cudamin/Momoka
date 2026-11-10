using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>Only generated local PCM media, with no user configuration, scripts, network or reports.</summary>
internal static class PlexMultipartNativeTests
{
    public static int Run(string dll)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            ValidateLocal(dll);
            if (!string.Equals(Path.GetFileName(dll), "libmpv-2.dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("只接受本地 libmpv-2.dll");
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "mpv-runtime", "approved-payload.json")))!;
            var approved = manifest["native"]!.AsArray().First(value => value!["path"]!.ToString() == "libmpv-2.dll")!;
            using var nativeFile = File.OpenRead(dll);
            var hash = Convert.ToHexString(SHA256.HashData(nativeFile));
            if (!hash.Equals(approved["sha256"]!.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("原生内核不符合本树批准载荷");
            Console.WriteLine("批准内核 " + dll + " SHA256=" + hash);
            var directory = Path.GetFullPath(Path.Combine("artifacts", "plex-multipart-native", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            ValidateLocal(directory);
            var first = Path.Combine(directory, "第一段;,=.wav");
            var second = Path.Combine(directory, "第二段.wav");
            WriteWave(first, 2);
            WriteWave(second, 3);
            var request = new PlaybackRequest
            {
                MediaUrl = new Uri(first),
                Title = "匿名生成分段探针",
                StartSeconds = 3.5,
                MediaParts = [new(new Uri(first), 2 * TimeSpan.TicksPerSecond), new(new Uri(second), 3 * TimeSpan.TicksPerSecond)],
                RunTimeTicks = 5 * TimeSpan.TicksPerSecond
            };
            var resource = MpvMediaResource.Build(request, address => address.LocalPath);
            LibMpvNative.UseLibrary(dll);
            var dllRoot = Path.GetDirectoryName(dll)!;
            LibMpvNative.EnsureDependencyDirectories(dllRoot, Path.Combine(dllRoot, "assets", "mpv-runtime"));
            var handle = LibMpvNative.mpv_create();
            if (handle == IntPtr.Zero) throw new InvalidOperationException("mpv_create 失败");
            var checks = 0;
            var ended = 0;
            var switchedSecond = false;
            void Check(bool condition, string note)
            {
                if (!condition) throw new InvalidOperationException(note);
                Console.WriteLine("通过 " + note);
                checks++;
            }
            try
            {
                foreach (var (key, value) in new[] { ("config", "no"), ("load-scripts", "no"), ("scripts", ""),
                    ("vo", "null"), ("ao", "null"), ("idle", "yes"), ("pause", "yes"), ("start", "3.5"),
                    ("terminal", "no"), ("input-default-bindings", "no"), ("input-terminal", "no"),
                    // EDL itself requires references. Every referenced file below was generated here
                    // as PCM; no media, playlist, subtitle, configuration or URL is accepted from callers.
                    ("osc", "no"), ("force-window", "no"), ("access-references", "yes"), ("ytdl", "no"),
                    ("sub-auto", "no"), ("audio-file-auto", "no"), ("http-header-fields", "") })
                    if (LibMpvNative.mpv_set_option_string(handle, key, value) < 0)
                        throw new InvalidOperationException("隔离选项失败 " + key);
                Check(LibMpvNative.mpv_initialize(handle) >= 0, "无用户配置与脚本的 null 音视频内核初始化");
                LibMpvNative.mpv_request_log_messages(handle, "v");
                void Command(params string[] args)
                {
                    var pointers = args.Select(Marshal.StringToCoTaskMemUTF8).Append(IntPtr.Zero).ToArray();
                    try
                    {
                        if (LibMpvNative.mpv_command(handle, pointers) < 0)
                            throw new InvalidOperationException("原生命令失败 " + args[0]);
                    }
                    finally { foreach (var pointer in pointers) if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
                }
                double? Number(string property)
                {
                    var value = LibMpvNative.mpv_get_property_string(handle, property);
                    try
                    {
                        return value != IntPtr.Zero && double.TryParse(Marshal.PtrToStringUTF8(value),
                            CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
                    }
                    finally { if (value != IntPtr.Zero) LibMpvNative.mpv_free(value); }
                }
                void Pump()
                {
                    var pointer = LibMpvNative.mpv_wait_event(handle, 0.02);
                    var current = Marshal.PtrToStructure<LibMpvNative.MpvEvent>(pointer);
                    if (current.EventId == LibMpvNative.EventEndFile)
                    {
                        var end = Marshal.PtrToStructure<LibMpvNative.MpvEventEndFile>(current.Data);
                        if (end.Reason != LibMpvNative.EndFileEof) throw new InvalidOperationException("播放失败 reason=" + end.Reason);
                        ended++;
                    }
                    if (current.EventId == LibMpvNative.EventLogMessage)
                    {
                        var log = Marshal.PtrToStructure<LibMpvNative.MpvEventLogMessage>(current.Data);
                        var message = Marshal.PtrToStringUTF8(log.Text) ?? "";
                        if (log.LogLevel <= 30) Console.Write(message);
                        switchedSecond |= message.Contains("switch to segment 1", StringComparison.Ordinal);
                    }
                }
                void Until(Func<bool> condition)
                {
                    var timeout = Stopwatch.StartNew();
                    while (!condition())
                    {
                        if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("原生分段探针超时");
                        Pump();
                    }
                }
                Command("loadfile", resource, "replace");
                Until(() => switchedSecond && Number("time-pos") is { } position && Math.Abs(position - 3.5) < 0.06);
                Check(Number("duration") is { } duration && Math.Abs(duration - 5) < 0.001, "两个分段呈现整片 5 秒时长");
                Check(switchedSecond, "起播 3.5 秒已定位第二段的 1.5 秒");
                Command("seek", "1.5", "absolute+exact");
                Until(() => Number("time-pos") is { } position && Math.Abs(position - 1.5) < 0.06);
                Check(ended == 0, "向前跨段 seek 保持播放生命周期");
                Command("seek", "2", "absolute+exact");
                Until(() => Number("time-pos") is { } position && Math.Abs(position - 2) < 0.06);
                Check(ended == 0, "恰好分段边界可 seek 且没有 EOF");
                Command("seek", "0", "absolute+exact");
                Until(() => Number("time-pos") is { } position && position < 0.06);
                Command("set", "pause", "no");
                Until(() => Number("time-pos") is > 2.2 || ended > 0);
                Check(ended == 0, "第一段自然结束后接续第二段且没有 end-file");
                Until(() => ended > 0);
                Check(ended == 1, "只有整片结束产生一次自然 EOF");
                Check(request.RunTimeTicks == 5 * TimeSpan.TicksPerSecond, "完成位置使用整片时长");
            }
            finally { LibMpvNative.mpv_terminate_destroy(handle); }
            Console.WriteLine($"共 {checks} 项通过；仅生成本地 PCM，未连接服务器或上报观看记录。");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void ValidateLocal(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("只接受本地绝对路径");
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new ArgumentException("不接受网络盘");
        for (FileSystemInfo? current = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
            current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("不接受重解析路径");
    }

    private static void WriteWave(string path, int seconds)
    {
        const int sampleRate = 8000;
        var samples = sampleRate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
        writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var index = 0; index < samples; index++) writer.Write((short)(Math.Sin(index * Math.Tau * 440 / sampleRate) * 1000));
    }
}
