using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>Generated PCM reaches the approved native core through anonymous loopback capabilities only.</summary>
internal static class PlexMultipartHttpNativeTests
{
    public static int Run(string dll)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { return RunAsync(dll).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string dll)
    {
        ValidateLibrary(dll);
        await using var origin = new PcmOrigin();
        var request = new PlaybackRequest
        {
            MediaUrl = origin.First,
            Title = "匿名受管分段探针",
            StartSeconds = 3.5,
            MediaParts = [new(origin.First, 2 * TimeSpan.TicksPerSecond), new(origin.Second, 3 * TimeSpan.TicksPerSecond)],
            RunTimeTicks = 5 * TimeSpan.TicksPerSecond,
            HttpHeaders = [new("X-Plex-Token", "placeholder-only")]
        };
        await using var transport = new PlaybackHttpTransport(request);
        var nativeRequest = transport.Prepare(request);
        var checks = 0;
        void Check(bool condition, string note)
        {
            if (!condition) throw new InvalidOperationException(note);
            Console.WriteLine("通过 " + note);
            checks++;
        }
        Check(nativeRequest.MediaParts.Count == 2 && nativeRequest.MediaParts.Zip(request.MediaParts)
            .All(pair => pair.First.MediaUrl != pair.Second.MediaUrl
                && pair.First.MediaUrl.IsLoopback && pair.First.DurationTicks == pair.Second.DurationTicks),
            "每个分段均通过真实受管转发映射，时长保持不变");
        Check(nativeRequest.HttpHeaders.Count == 0, "原生请求不包含服务器认证头");
        var resource = MpvMediaResource.Build(nativeRequest);
        LibMpvNative.UseLibrary(dll);
        var dllRoot = Path.GetDirectoryName(dll)!;
        LibMpvNative.EnsureDependencyDirectories(dllRoot, Path.Combine(dllRoot, "assets", "mpv-runtime"));
        var context = LibMpvNative.mpv_create();
        if (context == IntPtr.Zero) throw new InvalidOperationException("mpv_create 失败");
        var ended = 0;
        var switchedSecond = false;
        try
        {
            foreach (var (key, value) in new[]
            {
                ("config", "no"), ("load-scripts", "no"), ("scripts", ""), ("ytdl", "no"),
                ("vo", "null"), ("ao", "null"), ("idle", "yes"), ("pause", "yes"), ("start", "3.5"),
                ("terminal", "no"), ("log-file", ""), ("input-default-bindings", "no"), ("input-terminal", "no"),
                ("osc", "no"), ("force-window", "no"), ("sub-auto", "no"), ("audio-file-auto", "no"),
                ("http-header-fields", ""), ("http-proxy", ""), ("save-position-on-quit", "no"),
                ("resume-playback", "no"),
                // EDL requires references; all URLs are generated capabilities for the two PCM arrays.
                ("access-references", "yes")
            })
                if (LibMpvNative.mpv_set_option_string(context, key, value) < 0)
                    throw new InvalidOperationException("隔离选项失败 " + key);
            Check(LibMpvNative.mpv_initialize(context) >= 0, "null 音视频输出初始化，用户配置和脚本关闭");
            Check(Text("http-header-fields") == "", "原生全局 HTTP 请求头确认为空");
            LibMpvNative.mpv_request_log_messages(context, "v");

            string? Text(string property)
            {
                var pointer = LibMpvNative.mpv_get_property_string(context, property);
                try { return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer); }
                finally { if (pointer != IntPtr.Zero) LibMpvNative.mpv_free(pointer); }
            }
            double? Number(string property) => double.TryParse(Text(property), CultureInfo.InvariantCulture, out var value) ? value : null;
            void Command(params string[] arguments)
            {
                var pointers = arguments.Select(Marshal.StringToCoTaskMemUTF8).Append(IntPtr.Zero).ToArray();
                try
                {
                    if (LibMpvNative.mpv_command(context, pointers) < 0)
                        throw new InvalidOperationException("原生命令失败 " + arguments[0]);
                }
                finally { foreach (var pointer in pointers) if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
            }
            void Pump()
            {
                var pointer = LibMpvNative.mpv_wait_event(context, 0.02);
                var current = Marshal.PtrToStructure<LibMpvNative.MpvEvent>(pointer);
                if (current.EventId == LibMpvNative.EventEndFile)
                {
                    var end = Marshal.PtrToStructure<LibMpvNative.MpvEventEndFile>(current.Data);
                    if (end.Reason != LibMpvNative.EndFileEof)
                        throw new InvalidOperationException("原生分段失败 reason=" + end.Reason + ", error=" + end.Error);
                    ended++;
                }
                if (current.EventId == LibMpvNative.EventLogMessage)
                {
                    var log = Marshal.PtrToStructure<LibMpvNative.MpvEventLogMessage>(current.Data);
                    // Consume only the segment marker; diagnostics may contain capability addresses.
                    switchedSecond |= (Marshal.PtrToStringUTF8(log.Text) ?? "")
                        .Contains("switch to segment 1", StringComparison.Ordinal);
                }
            }
            void Until(Func<bool> condition)
            {
                var timeout = Stopwatch.StartNew();
                while (!condition())
                {
                    if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("受管原生分段探针超时");
                    Pump();
                }
            }
            Command("loadfile", resource, "replace");
            Until(() => switchedSecond && Number("time-pos") is { } position && Math.Abs(position - 3.5) < 0.06);
            Check(Number("duration") is { } duration && Math.Abs(duration - 5) < 0.001, "受管两段呈现整片 5 秒时长");
            Check(switchedSecond, "3.5 秒续播定位第二段 1.5 秒");
            Check(origin.Reads.Any(read => read.Part == 1) && origin.Reads.Any(read => read.Part == 2), "原生已读取两个注册分段");
            Command("seek", "1.5", "absolute+exact");
            Until(() => Number("time-pos") is { } position && Math.Abs(position - 1.5) < 0.06);
            Check(ended == 0, "向前跨段 seek 不结束播放生命周期");
            Command("seek", "2", "absolute+exact");
            Until(() => Number("time-pos") is { } position && Math.Abs(position - 2) < 0.06);
            Check(ended == 0, "分段边界 seek 没有 EOF");
            Command("seek", "0", "absolute+exact");
            Until(() => Number("time-pos") is { } position && position < 0.06);
            Command("set", "pause", "no");
            Until(() => Number("time-pos") is > 2.2 || ended > 0);
            Check(ended == 0, "首段自然结束接续第二段，没有 end-file");
            Until(() => ended > 0);
            Check(ended == 1, "整片自然结束仅产生一次 EOF");
            Check(origin.Reads.Count > 0 && origin.Reads.All(read => read.Authenticated), "来源只收到占位认证，所有分段与 Range 请求保持认证");
            Check(origin.Reads.Any(read => read.Ranged), "原生文件寻址已通过受管 Range 转发");
        }
        finally
        {
            transport.Cancel();
            LibMpvNative.mpv_terminate_destroy(context);
        }
        Console.WriteLine($"共 {checks} 项通过；仅自生成 PCM 与随机端口 loopback，无窗口、音频、真实服务器或观看记录写入。");
        return 0;
    }

    private static void ValidateLibrary(string dll)
    {
        if (!Path.IsPathFullyQualified(dll) || dll.StartsWith(@"\\", StringComparison.Ordinal)
            || new DriveInfo(Path.GetPathRoot(dll)!).DriveType == DriveType.Network
            || !string.Equals(Path.GetFileName(dll), "libmpv-2.dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("只接受本地绝对路径的 libmpv-2.dll");
        for (FileSystemInfo? entry = new FileInfo(dll); entry is not null;
            entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("不接受重解析路径");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine("assets", "mpv-runtime", "approved-payload.json")))!;
        var approved = manifest["native"]!.AsArray().First(value => value!["path"]!.ToString() == "libmpv-2.dll")!;
        using var source = File.OpenRead(dll);
        var hash = Convert.ToHexString(SHA256.HashData(source));
        if (!hash.Equals(approved["sha256"]!.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("原生内核不符合本树批准载荷");
        Console.WriteLine("批准内核 SHA256=" + hash);
    }

    private sealed record Read(int Part, bool Authenticated, bool Ranged);

    private sealed class PcmOrigin : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentDictionary<long, Task> _requests = new();
        private readonly Task _accept;
        private readonly byte[][] _parts = [Wave(2), Wave(3)];
        private long _requestId;
        internal ConcurrentBag<Read> Reads { get; } = [];
        internal Uri First { get; }
        internal Uri Second { get; }

        internal PcmOrigin()
        {
            _listener.Start();
            var root = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            First = new Uri(root + "/first.wav");
            Second = new Uri(root + "/second.wav");
            _accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false);
                    _requests[Interlocked.Increment(ref _requestId)] = ServeAsync(client);
                }
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var line = await reader.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                    var start = line?.Split(' ') ?? [];
                    var part = start.Length == 3 ? start[1] switch { "/first.wav" => 1, "/second.wav" => 2, _ => 0 } : 0;
                    if (part == 0 || start[0] is not ("GET" or "HEAD")) throw new InvalidOperationException("探针出现非注册资源请求");
                    var authenticated = false;
                    string? range = null;
                    while (await reader.ReadLineAsync(_lifetime.Token).ConfigureAwait(false) is { Length: > 0 } header)
                    {
                        if (header.StartsWith("X-Plex-Token:", StringComparison.OrdinalIgnoreCase))
                            authenticated = header[(header.IndexOf(':') + 1)..].Trim() == "placeholder-only";
                        if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = header[6..].Trim();
                    }
                    var bytes = _parts[part - 1];
                    var offset = 0;
                    var end = bytes.Length - 1;
                    if (range is not null)
                    {
                        if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || range.Contains(','))
                            throw new InvalidOperationException("探针出现不支持的 Range");
                        var values = range[6..].Split('-');
                        if (values.Length != 2 || !int.TryParse(values[0], CultureInfo.InvariantCulture, out offset))
                            throw new InvalidOperationException("探针出现非法 Range");
                        if (values[1].Length > 0 && !int.TryParse(values[1], CultureInfo.InvariantCulture, out end))
                            throw new InvalidOperationException("探针出现非法 Range");
                        end = Math.Min(end, bytes.Length - 1);
                    }
                    Reads.Add(new(part, authenticated, range is not null));
                    var valid = offset >= 0 && offset < bytes.Length && end >= offset;
                    var status = valid ? range is null ? "200 OK" : "206 Partial Content" : "416 Range Not Satisfiable";
                    var length = valid ? end - offset + 1 : 0;
                    var contentRange = range is null ? "" : valid ? $"Content-Range: bytes {offset}-{end}/{bytes.Length}\r\n" : $"Content-Range: bytes */{bytes.Length}\r\n";
                    var headers = $"HTTP/1.1 {status}\r\nContent-Type: audio/wav\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\n{contentRange}Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _lifetime.Token).ConfigureAwait(false);
                    if (start[0] == "GET" && valid)
                        await stream.WriteAsync(bytes.AsMemory(offset, length), _lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or SocketException) { }
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            _listener.Stop();
            await _accept.ConfigureAwait(false);
            await Task.WhenAll(_requests.Values).ConfigureAwait(false);
            _lifetime.Dispose();
        }

        private static byte[] Wave(int seconds)
        {
            const int sampleRate = 8000;
            var samples = sampleRate * seconds;
            using var memory = new MemoryStream();
            using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
            writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
            writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(samples * 2);
            for (var index = 0; index < samples; index++) writer.Write((short)(Math.Sin(index * Math.Tau * 440 / sampleRate) * 1000));
            return memory.ToArray();
        }
    }
}
