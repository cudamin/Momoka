using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>Optional current-DLL probe. Anonymous random-port loopback only, 404 with zero media bytes.</summary>
internal static class PlaybackHttpNativeProbe
{
    internal static int Run(string dll)
    {
        dll = Path.GetFullPath(dll);
        Assert.False(dll.StartsWith("\\\\", StringComparison.Ordinal), "探针DLL必须为本地文件");
        for (var entry = new FileInfo(dll) as FileSystemInfo; entry is not null;
            entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            Assert.False((entry.Attributes & FileAttributes.ReparsePoint) != 0, "探针DLL路径不得经过重解析点");
        var payload = JsonDocument.Parse(File.ReadAllText("assets/mpv-runtime/approved-payload.json"));
        var approved = payload.RootElement.GetProperty("native").EnumerateArray()
            .Single(entry => entry.GetProperty("path").GetString() == "libmpv-2.dll");
        Assert.Equal(approved.GetProperty("sha256").GetString()!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
        LibMpvNative.UseLibrary(Path.GetFullPath(dll));
        LibMpvNative.EnsureDependencyDirectories(Path.GetDirectoryName(Path.GetFullPath(dll))!, Path.GetFullPath("assets/mpv-runtime"));
        foreach (var name in new[] { "X-Emby-Token", "X-Emby-Authorization", "Authorization", "X-Plex-Token" })
            RunAsync(name).GetAwaiter().GetResult();
        Console.WriteLine("匿名受管+随包原生重定向探针：4/4通过，目标认证头0，无媒体正文，无窗口/音频/真实服务器");
        return 0;
    }

    private static async Task RunAsync(string name)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var target = new LocalEndpoint(name, null);
        await using var origin = new LocalEndpoint(name, target.Url);
        var request = new PlaybackRequest
        {
            MediaUrl = origin.Url,
            Title = "anonymous-http-redirect",
            HttpHeaders = [new(name, "placeholder-only")]
        };
        await using var transport = new PlaybackHttpTransport(request);
        var nativeRequest = transport.Prepare(request);
        var context = LibMpvNative.mpv_create();
        Assert.True(context != IntPtr.Zero);
        try
        {
            foreach (var (option, value) in new Dictionary<string, string>
            {
                ["config"] = "no",
                ["load-scripts"] = "no",
                ["scripts"] = "",
                ["ytdl"] = "no",
                ["vo"] = "null",
                ["ao"] = "null",
                ["audio"] = "no",
                ["terminal"] = "no",
                ["log-file"] = "",
                ["http-proxy"] = "",
                ["http-header-fields"] = "",
                ["input-ipc-server"] = "",
                ["access-references"] = "no",
                ["load-unsafe-playlists"] = "no",
                ["idle"] = "yes",
                ["save-position-on-quit"] = "no",
                ["resume-playback"] = "no"
            }) Assert.True(LibMpvNative.mpv_set_option_string(context, option, value) >= 0, "native option " + option);
            Assert.True(LibMpvNative.mpv_initialize(context) >= 0);
            Assert.True(LibMpvBackend.Run(context, "loadfile", nativeRequest.MediaUrl.AbsoluteUri, "replace"));
            var first = await origin.Received.WaitAsync(deadline.Token);
            var second = await target.Received.WaitAsync(deadline.Token);
            Assert.True(first, "同源收到占位认证头");
            Assert.False(second, "跨源目标不能收到认证头");
        }
        finally
        {
            transport.Cancel();
            LibMpvNative.mpv_terminate_destroy(context);
        }
    }

    private sealed class LocalEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _work;
        private readonly TaskCompletionSource<bool> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<bool> Received => _received.Task;
        internal Uri Url { get; }

        internal LocalEndpoint(string header, Uri? redirect)
        {
            _listener.Start();
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/fixture.mkv");
            _work = Task.Run(async () =>
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                    using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, leaveOpen: true);
                    var hasHeader = false;
                    while (await reader.ReadLineAsync(_lifetime.Token) is { Length: > 0 } line)
                        if (line.StartsWith(header + ":", StringComparison.OrdinalIgnoreCase)) hasHeader = true;
                    var reply = redirect is null ? "HTTP/1.1 404 Missing\r\n" : $"HTTP/1.1 302 Redirect\r\nLocation: {redirect.AbsoluteUri}\r\n";
                    await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(reply + "Content-Length: 0\r\nConnection: close\r\n\r\n"), _lifetime.Token);
                    _received.TrySetResult(hasHeader);
                }
                catch (OperationCanceledException) { }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            _listener.Stop();
            await _work;
            _lifetime.Dispose();
        }
    }
}
