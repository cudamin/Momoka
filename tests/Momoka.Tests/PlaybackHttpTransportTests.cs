using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlaybackHttpTransportTests
{
    public static void Register()
    {
        Case("分段认证：每段先映射再交给外部与内置播放器的 EDL", async () =>
        {
            var request = Request() with
            {
                MediaParts =
                [
                    new(Request().MediaUrl, 2 * TimeSpan.TicksPerSecond),
                    new(new Uri("https://origin.invalid/second.wav"), 3 * TimeSpan.TicksPerSecond)
                ]
            };
            using var endpoint = new Endpoint();
            await using var transport = new PlaybackHttpTransport(request, endpoint);
            var native = transport.Prepare(request);
            Assert.Equal(2, native.MediaParts.Count);
            Assert.True(native.MediaParts.All(part => part.MediaUrl.IsLoopback));
            Assert.Equal(0, native.HttpHeaders.Count);
            var resource = MpvMediaResource.Build(native);
            Assert.DoesNotContain("origin.invalid", resource);
            Assert.DoesNotContain("placeholder-only", resource);
            Assert.Equal(resource, (string)MpvArgumentBuilder.LoadCommands(native)[^1][1]!);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            foreach (var part in native.MediaParts) await client.GetByteArrayAsync(part.MediaUrl);
            Assert.Equal(2, endpoint.Sent.Count);
            Assert.True(endpoint.Sent.All(sent => sent.Headers.ContainsKey("X-Emby-Token")));
        });
        Test("播放认证边界：外部 mpv 永远不持有服务器 HTTP 头", () =>
        {
            var commands = MpvArgumentBuilder.LoadCommands(Request());
            Assert.Equal(0, ((string[])commands[0][2]!).Length);
        });
        Test("播放认证边界：独占同窗换片清空服务器 HTTP 头", () =>
        {
            var headers = InlineSwitch.PerFile(Request()).Single(option => option.Key == "http-header-fields");
            Assert.Equal("", headers.Value);
        });
        Test("播放认证边界：自定义配置组不能恢复native代理与HTTP头", () =>
        {
            Assert.True(MpvCustomConfig.IsHostOption("http-proxy"));
            Assert.True(MpvCustomConfig.IsHostOption("http-header-fields-append"));
            Assert.True(MpvArgumentBuilder.Build(Request(), "pipe-fixture").Contains("--http-proxy="));
            var root = Directory.GetCurrentDirectory();
            var source = File.ReadAllText(Path.Combine(root, "src/Momoka.Core/Playback/LibMpvBackend.cs"));
            Assert.Contains("httpTransport?.Prepare(request)", source);
            Assert.Contains("ApplyOptions(context, nativeRequest", source);
            Assert.Contains("mpv_set_option_string(context, \"http-header-fields\", \"\")", source);
            Assert.Contains("mpv_set_option_string(context, \"http-proxy\", \"\")", source);
        });
        Case("播放认证边界：外部真实事件回调反映射外挂字幕文件名", async () =>
        {
            using var endpoint = new Endpoint();
            await using var transport = new PlaybackHttpTransport(Request(), endpoint);
            var file = new Uri("https://origin.invalid/sub.srt");
            var mapped = transport.Map(file);
            using var process = new System.Diagnostics.Process();
            var handle = new MpvProcessHandle(process) { HttpTransport = transport };
            string? observed = null;
            handle.TracksChanged += tracks => observed = tracks.Single().ExternalFilename;
            var raw = System.Text.Json.JsonSerializer.Serialize(new
            {
                @event = "property-change",
                name = "track-list",
                data = new[] { new Dictionary<string, object> { ["id"] = 1, ["type"] = "sub", ["external-filename"] = mapped } }
            });
            Assert.True(MpvIpcMessage.TryParse(raw, out var message));
            typeof(MpvProcessHandle).GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(handle, ["track-list", message]);
            Assert.Equal(file.AbsoluteUri, observed);
        });
        foreach (var code in new[] { 301, 302, 303, 307, 308 })
            Case($"播放HTTP：{code}同源保留认证，跨源及回跳永不恢复", async () =>
            {
                using var endpoint = new Endpoint();
                endpoint.Reply = (request, index) => index switch
                {
                    0 => Redirect(code, "/same.mkv"),
                    1 => Redirect(code, "https://cdn.invalid/foreign.mkv"),
                    2 => Redirect(code, "https://origin.invalid/back.mkv"),
                    _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("answer") }
                };
                await using var transport = new PlaybackHttpTransport(Request(), endpoint);
                using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                Assert.Equal("answer", await client.GetStringAsync(transport.Map(Request().MediaUrl)));
                Assert.Equal(4, endpoint.Sent.Count);
                Assert.True(endpoint.Sent.Take(2).All(request => request.Headers.ContainsKey("X-Emby-Token")));
                Assert.True(endpoint.Sent.Skip(2).All(request => !request.Headers.ContainsKey("X-Emby-Token")));
            });
        Case("播放HTTP：初始外源字幕不带认证、动态映射与反映射保留身份", async () =>
        {
            using var endpoint = new Endpoint();
            await using var transport = new PlaybackHttpTransport(Request(), endpoint);
            var foreign = new Uri("https://cdn.invalid/sub.srt");
            var mapped = transport.Map(foreign);
            Assert.Equal(foreign.AbsoluteUri, transport.Original(mapped));
            Assert.DoesNotContain("placeholder-only", mapped);
            var command = transport.MapCommand(["sub-add", foreign.AbsoluteUri, "select"]);
            Assert.Equal(mapped, command[1]);
            Assert.Equal("C:\\local.srt", transport.MapCommand(["sub-add", "C:\\local.srt"])[1]);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            await client.GetByteArrayAsync(mapped);
            Assert.False(endpoint.Sent.Single().Headers.ContainsKey("X-Emby-Token"));
        });
        Case("播放HTTP：Range、HEAD、206与416保留范围，不传cookie或跳转头", async () =>
        {
            using var endpoint = new Endpoint();
            endpoint.Reply = (_, index) =>
            {
                var response = new HttpResponseMessage(index == 2 ? HttpStatusCode.RequestedRangeNotSatisfiable : HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent(index == 2 ? [] : [1, 2, 3]) };
                response.Content.Headers.ContentRange = index == 2 ? new ContentRangeHeaderValue(10) : new ContentRangeHeaderValue(2, 4, 10);
                response.Headers.AcceptRanges.Add("bytes");
                response.Headers.TryAddWithoutValidation("Set-Cookie", "synthetic=value");
                return response;
            };
            await using var transport = new PlaybackHttpTransport(Request(), endpoint);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            var url = transport.Map(Request().MediaUrl);
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head, HttpMethod.Get })
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.Range = new RangeHeaderValue(2, 4);
                using var response = await client.SendAsync(request);
                Assert.Equal(method, endpoint.Sent[^1].Method);
                Assert.Equal("bytes=2-4", endpoint.Sent[^1].Headers["Range"]);
                Assert.False(response.Headers.Contains("Set-Cookie"));
                Assert.False(response.Headers.Contains("Location"));
                Assert.True(response.Content.Headers.ContentRange is not null);
                if (method == HttpMethod.Head) Assert.Equal(0, (await response.Content.ReadAsByteArrayAsync()).Length);
            }
        });
        Case("播放HTTP：任意路径不取资源，嵌套清单明确拒绝", async () =>
        {
            using var endpoint = new Endpoint();
            endpoint.Reply = (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"https://foreign.invalid/key\"\nsegment.ts") };
            await using var transport = new PlaybackHttpTransport(Request(), endpoint);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            var url = transport.Map(Request().MediaUrl);
            Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(new Uri(url), "unregistered.ts"))).StatusCode);
            Assert.Equal(1, endpoint.Sent.Count);
            Assert.Throws<InvalidOperationException>(() => transport.Map(new Uri("https://origin.invalid/file.m3u8")));
        });
        Case("播放HTTP：销毁取消等待HTTP头的请求并封闭能力地址", async () =>
        {
            using var endpoint = new Endpoint();
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            endpoint.Wait = async token =>
            {
                received.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            };
            var transport = new PlaybackHttpTransport(Request(), endpoint);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            var pending = client.GetAsync(transport.Map(Request().MediaUrl));
            await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await transport.DisposeAsync();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try { await pending; } catch (HttpRequestException) { }
            Assert.Throws<ObjectDisposedException>(() => transport.Map(Request().MediaUrl));
        });
        Case("播放HTTP：本地文件无认证转发，seek中断单次请求立即取消上游", async () =>
        {
            Assert.True(PlaybackHttpTransport.Create(Request() with { MediaUrl = new Uri("file:///C:/fixture.mkv") }) is null);
            using var endpoint = new Endpoint();
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            endpoint.Wait = async token =>
            {
                received.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            };
            await using var transport = new PlaybackHttpTransport(Request(), endpoint);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            using var abort = new CancellationTokenSource();
            var pending = client.GetAsync(transport.Map(Request().MediaUrl), abort.Token);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            abort.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        });
        Case("播放HTTP：Stop与Dispose并发和迟到Cancel幂等", async () =>
        {
            using var endpoint = new Endpoint();
            var transport = new PlaybackHttpTransport(Request(), endpoint);
            await Task.WhenAll(Task.Run(transport.Cancel), transport.DisposeAsync().AsTask());
            transport.Cancel();
            await transport.DisposeAsync();
        });
    }

    private static PlaybackRequest Request() => new()
    {
        MediaUrl = new Uri("https://origin.invalid/video.mkv"),
        Title = "offline",
        HttpHeaders = [new("X-Emby-Token", "placeholder-only")]
    };

    private static void Case(string name, Func<Task> run) => Test(name, () => run().GetAwaiter().GetResult());
    private static HttpResponseMessage Redirect(int code, string location) => new((HttpStatusCode)code)
    { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private sealed record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers);
    private sealed class Endpoint : HttpMessageHandler
    {
        internal List<Sent> Sent { get; } = [];
        internal Func<HttpRequestMessage, int, HttpResponseMessage> Reply { get; set; } = (_, _) =>
            new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        internal Func<CancellationToken, Task>? Wait { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var index = Sent.Count;
            Sent.Add(new(request.Method, request.RequestUri!, request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(',', pair.Value), StringComparer.OrdinalIgnoreCase)));
            if (Wait is { } wait) await wait(token);
            return Reply(request, index);
        }
    }
}
