using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Production transport and service, anonymous loopback only; receipts are final after accept tasks join.</summary>
internal static class MoviePilotCookieOwnershipTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);

    public static void Register()
    {
        foreach (var native in new[] { "default", "sockets", "native" })
            Test("MP Cookie：换同主机端口不发送旧凭据 " + native,
                () => CrossPortAsync(native).GetAwaiter().GetResult());
        Test("MP Cookie：同源换账号与 A→B→A 均使用新容器",
            () => AccountAsync().GetAwaiter().GetResult());
        Test("MP Cookie：旧在飞响应不能重新种入新账号的图片与 SSE",
            () => LateAsync().GetAwaiter().GetResult());
        Test("MP Cookie：底层直接登录后旧令牌仍绑定原账号 Cookie",
            () => DirectAsync().GetAwaiter().GetResult());
        Test("MP Cookie：图片委托在账号 ABA 后不能继承新代次",
            () => CapturedImageAsync().GetAwaiter().GetResult());
        Test("MP Cookie：带路径 API 基址的直接令牌仍续种所属 Cookie",
            () => DirectPrefixAsync().GetAwaiter().GetResult());
        foreach (var address in new[] { "", "http://[invalid" })
            Test("MP 图片所有者：未配置地址可捕获但永远不发请求 " + address, () =>
            {
                using var client = new MoviePilotClient(new RejectTransport());
                using var service = Service(client, new Uri("http://192.0.2.10:3001/"), out var settings);
                settings.MoviePilot.Url = address;
                var fetch = service.CaptureImageFetcher();
                Assert.Throws<OperationCanceledException>(() => fetch("https://image.invalid/old.jpg", default).GetAwaiter().GetResult());
            });
    }

    private static MoviePilotClient Client(string native) => native switch
    {
        "sockets" => new MoviePilotClient(new SocketsHttpHandler()),
        "native" => new MoviePilotClient(new HttpClientHandler()),
        _ => new MoviePilotClient()
    };

    private static MoviePilotService Service(MoviePilotClient client, Uri origin, out AppSettings settings)
    {
        settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = origin.AbsoluteUri;
        settings.MoviePilot.Username = "anonymous-A";
        settings.MoviePilot.ProtectedPassword = "fixture-password";
        return new MoviePilotService(client, new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings);
    }

    private static async Task CrossPortAsync(string native)
    {
        await using var a = new CookieServer();
        await using var b = new CookieServer();
        using var client = Client(native);
        using var service = Service(client, a.Origin, out var settings);
        await service.TestConnectionAsync(default);
        settings.MoviePilot.Url = b.Origin.AbsoluteUri;
        settings.MoviePilot.Username = "anonymous-B";
        await service.TestConnectionAsync(default);
        await service.FetchImageAsync("https://image.invalid/fixture.jpg", default);
        await a.StopAsync();
        await b.StopAsync();
        a.VerifyFinal();
        b.VerifyFinal();
        Assert.Equal(4, a.Requests.Length);
        Assert.Equal(5, b.Requests.Length);
        Assert.False(b.Requests.Any(receipt => receipt.OldCookie), "B 的首登录、读取及图片均不能携带 A Cookie");
        Assert.True(b.Requests.Last().OwnCookie, "新账号的资源 Cookie 仍必须支持图片");
    }

    private static async Task AccountAsync()
    {
        await using var server = new CookieServer();
        using var client = new MoviePilotClient();
        using var service = Service(client, server.Origin, out var settings);
        await service.TestConnectionAsync(default);
        settings.MoviePilot.Username = "anonymous-B";
        await service.TestConnectionAsync(default);
        settings.MoviePilot.Username = "anonymous-A";
        await service.TestConnectionAsync(default);
        await server.StopAsync();
        server.VerifyFinal();
        var logins = server.Requests.Where(receipt => receipt.Login).ToArray();
        Assert.Equal(3, logins.Length);
        Assert.True(logins.All(receipt => !receipt.OldCookie && !receipt.OwnCookie), "每个配置代次的首登录均无前代 Cookie");
        Assert.True(server.Requests.Any(receipt => !receipt.Login && receipt.OldCookie), "同账号 API 仍复用资源 Cookie");
    }

    private static async Task DirectAsync()
    {
        await using var server = new CookieServer();
        using var client = new MoviePilotClient();
        var a = await client.SignInAsync(server.Origin, "anonymous-A", "fixture-password", default);
        var b = await client.SignInAsync(server.Origin, "anonymous-B", "fixture-password", default);
        await client.GetAsync(server.Origin, a.AccessToken, "dashboard/system", default);
        await client.GetAsync(server.Origin, b.AccessToken, "dashboard/system", default);
        await client.GetAsync(server.Origin, "unknown-fixture-token", "dashboard/system", default);
        await server.StopAsync();
        server.VerifyFinal();
        var api = server.Requests.Where(receipt => !receipt.Login).ToArray();
        Assert.True(api[0].OldCookie && !api[0].OwnCookie);
        Assert.True(api[1].OwnCookie && !api[1].OldCookie);
        Assert.False(api[2].OwnCookie || api[2].OldCookie, "未知令牌不能借用最新账号 Cookie");
    }

    private static async Task CapturedImageAsync()
    {
        await using var server = new CookieServer();
        using var client = new MoviePilotClient();
        using var service = Service(client, server.Origin, out var settings);
        await service.TestConnectionAsync(default);
        // Bind the actual public image method. Once it returns an ownership delegate this invokes
        // that delegate; on the baseline the same operation captures only the old stable stamp.
        var method = typeof(MoviePilotService).GetMethod("CaptureImageFetcher");
        var fetch = method is null
            ? (Func<string, CancellationToken, Task<byte[]>>)((url, token) => service.FetchImageAsync(url, token))
            : (Func<string, CancellationToken, Task<byte[]>>)method.Invoke(service, null)!;
        settings.MoviePilot.Username = "anonymous-B";
        await service.TestConnectionAsync(default);
        settings.MoviePilot.Username = "anonymous-A";
        await service.TestConnectionAsync(default);
        var refused = false;
        try { await fetch("https://image.invalid/old.jpg", default); }
        catch (OperationCanceledException) { refused = true; }
        if (method is not null)
        {
            var fresh = (Func<string, CancellationToken, Task<byte[]>>)method.Invoke(service, null)!;
            await fresh("https://image.invalid/new.jpg", default);
        }
        await server.StopAsync();
        server.VerifyFinal();
        Assert.True(refused, "旧图片委托不可采用ABA的新传输代次");
        Assert.Equal(method is null ? 12 : 13, server.Requests.Length);
    }

    private static async Task DirectPrefixAsync()
    {
        await using var server = new CookieServer();
        using var client = new MoviePilotClient();
        var apiBase = new Uri(server.Origin, "moviepilot/");
        var session = await client.SignInAsync(apiBase, "anonymous-A", "fixture-password", default);
        await client.GetAsync(apiBase, session.AccessToken, "dashboard/system", default);
        await server.StopAsync();
        server.VerifyFinal();
        Assert.True(server.Requests.Last().OldCookie, "反代路径基址也必须按来源找回同一登录 Cookie");
    }

    private static async Task LateAsync()
    {
        await using var server = new CookieServer { HoldOldSource = true };
        using var client = new MoviePilotClient();
        using var service = Service(client, server.Origin, out var settings);
        await service.TestConnectionAsync(default);
        var old = service.MediaSourcesAsync(default);
        await server.Held.Task.WaitAsync(Deadline);
        settings.MoviePilot.Username = "anonymous-B";
        await service.TestConnectionAsync(default);
        server.Release.TrySetResult();
        try { await old; throw new AssertionException("旧读取必须被连接身份检查拒绝"); }
        catch (OperationCanceledException) { }
        await service.FetchImageAsync("https://image.invalid/fixture.jpg", default);
        await service.SearchByKeywordAsync("fixture", default, progress: new InlineProgress());
        await server.StopAsync();
        server.VerifyFinal();
        var afterChange = server.Requests.Where(receipt => receipt.UserB).ToArray();
        Assert.True(afterChange.Length >= 6);
        Assert.False(afterChange.Any(receipt => receipt.OldCookie), "迟到 Set-Cookie 只能写回旧所有者");
        Assert.True(afterChange.Any(receipt => receipt.Image && receipt.OwnCookie));
        Assert.True(afterChange.Any(receipt => receipt.Stream && receipt.OwnCookie));
    }

    private sealed class InlineProgress : IProgress<MoviePilotSearchProgress>
    {
        public void Report(MoviePilotSearchProgress value) { }
    }

    private sealed class RejectTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("没有可用配置时不能发网络请求");
    }

    private sealed record Receipt(bool Login, bool UserB, bool OldCookie, bool OwnCookie, bool Image, bool Stream);

    private sealed class CookieServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<Receipt> _requests = new();
        private readonly List<Task> _handlers = [];
        private readonly Task _accept;
        private Task? _finish;
        private int _accepted;
        private bool _pending;
        private bool _userB;
        public bool HoldOldSource { get; init; }
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CookieServer()
        {
            _listener.Start();
            Origin = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _accept = AcceptAsync();
        }
        public Uri Origin { get; }
        public Receipt[] Requests => _requests.ToArray();
        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _accepted);
                    _handlers.Add(HandleAsync(socket));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task HandleAsync(TcpClient socket)
        {
            using (socket)
            {
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var first = await reader.ReadLineAsync(_stop.Token) ?? throw new InvalidDataException("Missing request");
                var path = first.Split(' ')[1];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                {
                    var colon = line.IndexOf(':');
                    headers[line[..colon]] = line[(colon + 1)..].Trim();
                }
                var length = int.Parse(headers.GetValueOrDefault("Content-Length", "0"), CultureInfo.InvariantCulture);
                if (length > 65536 || headers.ContainsKey("Transfer-Encoding")) throw new InvalidDataException("Invalid fixture framing");
                var body = new char[length];
                var offset = 0;
                while (offset < length)
                {
                    var count = await reader.ReadAsync(body.AsMemory(offset), _stop.Token);
                    if (count == 0) throw new EndOfStreamException();
                    offset += count;
                }
                var login = path.EndsWith("login/access-token", StringComparison.Ordinal);
                var authorization = headers.GetValueOrDefault("Authorization", "");
                var userB = login ? new string(body).Contains("anonymous-B", StringComparison.Ordinal)
                    : authorization.Length > 0 ? authorization.Contains("fixture-B", StringComparison.Ordinal) : _userB;
                if (login) _userB = userB;
                var cookie = headers.GetValueOrDefault("Cookie", "");
                var image = path.Contains("system/cache/image", StringComparison.Ordinal);
                var streaming = path.Contains("/stream", StringComparison.Ordinal);
                _requests.Enqueue(new Receipt(login, userB, cookie.Contains("resource-A", StringComparison.Ordinal),
                    cookie.Contains("resource-B", StringComparison.Ordinal), image, streaming));
                if (HoldOldSource && !userB && path.Contains("media/source", StringComparison.Ordinal))
                {
                    Held.TrySetResult();
                    await Release.Task.WaitAsync(_stop.Token);
                }
                var reply = login ? JsonSerializer.Serialize(new { access_token = userB ? "fixture-B" : "fixture-A" })
                    : streaming ? "data: {\"type\":\"done\",\"items\":[]}\n\n"
                    : image ? "fixture-image" : path.Contains("dashboard/system", StringComparison.Ordinal)
                    ? "{\"success\":true,\"data\":{\"version\":\"fixture\"}}" : "{\"success\":true,\"data\":[]}";
                var bytes = Encoding.UTF8.GetBytes(reply);
                var response = $"HTTP/1.1 200 OK\r\nContent-Type: {(streaming ? "text/event-stream" : "application/json")}\r\n" +
                    $"Content-Length: {bytes.Length}\r\nConnection: close\r\nSet-Cookie: MoviePilot=resource-{(userB ? "B" : "A")}; Path=/; HttpOnly\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                await stream.WriteAsync(bytes, _stop.Token);
            }
        }
        public Task StopAsync() => _finish ??= StopCoreAsync();
        private async Task StopCoreAsync()
        {
            _pending = _listener.Pending();
            await _stop.CancelAsync();
            _listener.Stop();
            await _accept.WaitAsync(Deadline);
            await Task.WhenAll(_handlers).WaitAsync(Deadline);
            _stop.Dispose();
        }
        public void VerifyFinal()
        {
            Assert.True(_finish?.IsCompletedSuccessfully == true);
            Assert.False(_pending, "收尾不能漏掉排队连接");
            Assert.Equal(_accepted, Requests.Length);
        }
        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
