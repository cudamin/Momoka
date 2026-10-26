using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class EmbyRealtimeTests
{
    private static void RegisterRedirectTransport()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            foreach (var status in new[] { 301, 302, 303, 307, 308 })
            {
                foreach (var sameOrigin in new[] { false, true })
                {
                    var target = sameOrigin ? "同源" : "跨端口来源";
                    Test($"实时握手边界：{kind} {status} {target}不自动重发，第二站请求与凭据均为零", () =>
                        VerifyRedirectAsync(kind, status, sameOrigin).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
                }
            }
        }

        Test("实时握手边界：等待首站响应时取消立即终止，不等待握手截止", () =>
            VerifyHandshakeCancellationAsync().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
    }

    /// <summary>运行真正的一次握手，避免后台重连把“回到原端点”误计为同源下一跳。</summary>
    private static Task ListenOnce(EmbyRealtimeConnection connection, CancellationToken token) =>
        (Task)typeof(EmbyRealtimeConnection).GetMethod("ListenOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(connection, [token])!;

    private static async Task VerifyRedirectAsync(MediaServerKind kind, int status, bool sameOrigin)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        var receiver = sameOrigin ? fixture.Listener : target;
        var port = ((IPEndPoint)receiver.LocalEndpoint).Port;
        var location = sameOrigin ? "/proxy/redirect-target" : $"http://127.0.0.1:{port}/redirect-target";
        var changes = 0;
        await using var connection = new EmbyRealtimeConnection(fixture.Session.Capture(), _ => changes++);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var once = ListenOnce(connection, deadline.Token);
        using var origin = await ReadRedirectRequestAsync(fixture.Listener, deadline.Token);
        Assert.True(origin.RequestLine.StartsWith("GET ", StringComparison.Ordinal));
        Assert.DoesNotContain("synthetic-realtime-old", origin.RequestLine);
        Assert.DoesNotContain("api_key", origin.RequestLine);
        if (kind == MediaServerKind.Jellyfin)
        {
            Assert.Contains("Token=\"synthetic-realtime-old\"", origin.Headers["Authorization"]);
            Assert.False(origin.Headers.ContainsKey("X-Emby-Token"));
        }
        else
        {
            Assert.Equal("synthetic-realtime-old", origin.Headers["X-Emby-Token"]);
            Assert.True(origin.Headers.ContainsKey("X-Emby-Authorization"));
        }

        using var observationLife = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var observation = ObserveRedirectHopAsync(receiver, observationLife.Token);
        await origin.ReplyAsync($"HTTP/1.1 {status} Fixture Redirect\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", deadline.Token);
        Exception? failure = null;
        try { await once.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (WebSocketException error) { failure = error; }
        finally { observationLife.Cancel(); }
        var hop = await observation;
        Assert.True(failure is WebSocketException, "重定向必须拒绝握手，不能采用第二站成功响应");
        Assert.Equal(0, hop.Requests, $"{kind}/{status}/{(sameOrigin ? "same" : "cross")}: next={hop.Requests}, credential-fields={hop.CredentialFields}");
        Assert.Equal(0, hop.CredentialFields, "原来源身份字段不能出现在下一站");
        Assert.False(receiver.Pending(), "结束时不得还留着未接收的下一跳连接");
        Assert.Equal(0, changes, "未升级的重定向响应不产生库变更通知");
    }

    private static async Task VerifyHandshakeCancellationAsync()
    {
        using var fixture = new RealtimeFixture(MediaServerKind.Emby);
        await fixture.RestoreAsync();
        await using var connection = new EmbyRealtimeConnection(fixture.Session.Capture(), _ => { });
        using var life = new CancellationTokenSource();
        var once = ListenOnce(connection, life.Token);
        using var acceptDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = await ReadRedirectRequestAsync(fixture.Listener, acceptDeadline.Token);
        life.Cancel();
        var canceled = false;
        try { await once.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled, "等待服务端响应时应由原请求取消结束");
        Assert.False(fixture.Listener.Pending());
    }

    private static async Task<(int Requests, int CredentialFields)> ObserveRedirectHopAsync(TcpListener listener, CancellationToken token)
    {
        var accepted = false;
        try
        {
            using var request = await ReadRedirectRequestAsync(listener, token, () => accepted = true);
            var credentials = request.Headers.Keys.Count(key => key.Equals("X-Emby-Token", StringComparison.OrdinalIgnoreCase)
                || key.Equals("X-Emby-Authorization", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
            await request.ReplyAsync("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", token);
            return (1, credentials);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return (accepted ? 1 : 0, 0); }
    }

    /// <summary>匿名双回环只观察实际 HTTP 握手；不读取保存账号或连接外部服务器。</summary>
    private static async Task<RedirectRequest> ReadRedirectRequestAsync(TcpListener listener, CancellationToken token, Action? onAccepted = null)
    {
        var client = await listener.AcceptTcpClientAsync(token);
        onAccepted?.Invoke();
        try
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];
            var complete = false;
            while (bytes.Count < 16384)
            {
                if (await client.GetStream().ReadAsync(buffer, token) == 0) throw new IOException("匿名握手未完成就关闭");
                bytes.Add(buffer[0]);
                if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                {
                    complete = true;
                    break;
                }
            }
            if (!complete) throw new IOException("匿名握手超过头部限额");
            var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var headers = lines.Skip(1).Select(line => line.Split(':', 2))
                .ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
            return new RedirectRequest(client, lines[0], headers);
        }
        catch { client.Dispose(); throw; }
    }

    private sealed class RedirectRequest(TcpClient client, string requestLine, Dictionary<string, string> headers) : IDisposable
    {
        public string RequestLine { get; } = requestLine;
        public Dictionary<string, string> Headers { get; } = headers;
        public Task ReplyAsync(string response, CancellationToken token) =>
            client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response), token).AsTask();
        public void Dispose() => client.Dispose();
    }
}
