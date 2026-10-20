using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class EmbyRealtimeTests
{
    private static void RegisterTransport()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            Test($"实时握手：{kind} 回环连接只经请求头鉴权，重连读取新令牌且不换来源", () =>
                VerifyHandshakeAsync(kind).WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());

        Test("实时生命周期：来源退出立即结束正在收消息的连接，不等十分钟空闲超时", () =>
            VerifySignOutAsync(connected: true).WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());
        Test("实时生命周期：握手失败的退避期间退出，不再发下一次握手", () =>
            VerifySignOutAsync(connected: false).WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());
    }

    private static async Task VerifyHandshakeAsync(MediaServerKind kind)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        var changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new EmbyRealtimeConnection(source, key => changed.TrySetResult(key));
        connection.Start();
        using var first = await fixture.AcceptAsync();
        CheckHandshake(first, source.CurrentConnection, "synthetic-realtime-old");
        fixture.Session.ApplyPasswordChange(source, "synthetic-new-password", "synthetic-realtime-new");
        Assert.Equal("synthetic-realtime-old", source.Connection.AccessToken, "捕获时快照不被改写");
        first.Socket.Abort();
        using var second = await fixture.AcceptAsync();
        CheckHandshake(second, source.CurrentConnection, "synthetic-realtime-new");
        await second.Socket.SendAsync(Encoding.UTF8.GetBytes("{\"MessageType\":\"Sessions\"}").AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None);
        await second.Socket.SendAsync(Encoding.UTF8.GetBytes("{\"MessageType\":\"LibraryChanged\"}").AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal(source.Key, await changed.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(source.IsAvailable && source.IsCurrent);
    }

    private static async Task VerifySignOutAsync(bool connected)
    {
        using var fixture = new RealtimeFixture(MediaServerKind.Emby);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        await using var connection = new EmbyRealtimeConnection(source, _ => throw new AssertionException("退出测试不应产生库变更"));
        connection.Start();
        using var accepted = await fixture.AcceptAsync(upgrade: connected);
        fixture.Session.SignOut(fixture.Server, fixture.Account);
        var loop = (Task)typeof(EmbyRealtimeConnection).GetField("_loop", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(connection)!;
        await loop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(source.IsAvailable);
        await Task.Delay(1200);
        Assert.False(fixture.Listener.Pending(), "来源退出后不得继续握手重连");
        Assert.Throws<OperationCanceledException>(() => _ = source.CurrentConnection);
    }

    private static void CheckHandshake(AcceptedSocket accepted, EmbyConnection connection, string token)
    {
        Assert.Equal("GET " + EmbyRealtimeProtocol.Build(connection.ApiBase, connection.Device.DeviceId, connection.ServerKind).PathAndQuery
            + " HTTP/1.1", accepted.RequestLine);
        Assert.DoesNotContain(token, accepted.RequestLine);
        Assert.DoesNotContain("api_key", accepted.RequestLine);
        if (connection.IsJellyfin)
        {
            Assert.Contains("MediaBrowser ", accepted.Headers["Authorization"]);
            Assert.Contains("Token=\"" + token + "\"", accepted.Headers["Authorization"]);
            Assert.False(accepted.Headers.ContainsKey("X-Emby-Token"));
        }
        else
        {
            Assert.Equal(token, accepted.Headers["X-Emby-Token"]);
            Assert.Contains("MediaBrowser ", accepted.Headers["X-Emby-Authorization"]);
        }
    }

    private sealed class AcceptedSocket(TcpClient client, WebSocket socket, string requestLine, Dictionary<string, string> headers) : IDisposable
    {
        public WebSocket Socket { get; } = socket;
        public string RequestLine { get; } = requestLine;
        public Dictionary<string, string> Headers { get; } = headers;
        public void Dispose() { Socket.Abort(); Socket.Dispose(); client.Dispose(); }
    }

    /// <summary>只监听 127.0.0.1 的随机端口；REST 恢复用假传输，不连接任何用户服务器。</summary>
    private sealed class RealtimeFixture : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "realtime-fixtures", Guid.NewGuid().ToString("N"));
        public TcpListener Listener { get; } = new(IPAddress.Loopback, 0);
        public ServerProfile Server { get; }
        public AccountProfile Account { get; } = new() { UserId = "u", Username = "fixture" };
        public EmbySession Session { get; }

        public RealtimeFixture(MediaServerKind kind)
        {
            Listener.Start();
            Server = new ServerProfile
            {
                Url = $"http://127.0.0.1:{((IPEndPoint)Listener.LocalEndpoint).Port}/proxy/",
                ServerKind = kind,
                ServerVersion = kind == MediaServerKind.Jellyfin ? "12.2" : "4.10.1.0",
                Name = "synthetic-realtime"
            };
            var settings = new AppSettings { AggregateMode = true };
            settings.Servers.Clear();
            settings.Servers.Add(Server);
            Server.Accounts.Add(Account);
            var vault = new CredentialVault(PassthroughSecretProtector.Instance);
            vault.SetAccessToken(Account, "synthetic-realtime-old");
            Directory.CreateDirectory(_root);
            Session = new EmbySession(settings,
                new SettingsStore(new AppPaths(_root), PassthroughSecretProtector.Instance), vault,
                DeviceIdentity.Create("synthetic-realtime-device", "1"), new RealtimeRest(kind));
        }

        public async Task RestoreAsync() => Assert.True(await Session.TryRestoreAsync(Server, Account, CancellationToken.None));

        public async Task<AcceptedSocket> AcceptAsync(bool upgrade = true)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var client = await Listener.AcceptTcpClientAsync(deadline.Token);
            try
            {
                var stream = client.GetStream();
                var bytes = new List<byte>();
                var buffer = new byte[1];
                while (bytes.Count < 16384)
                {
                    if (await stream.ReadAsync(buffer, deadline.Token) == 0) throw new IOException("回环握手提前断开");
                    bytes.Add(buffer[0]);
                    if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n') break;
                }
                if (bytes.Count >= 16384) throw new IOException("回环握手超过头部限额");
                var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var headers = lines.Skip(1).Select(line => line.Split(':', 2))
                    .ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
                var response = "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                if (upgrade)
                {
                    // SHA-1 is mandated by the WebSocket accept handshake, not a credential hash.
                    var hash = SHA1.HashData(Encoding.ASCII.GetBytes(headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
                    response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "
                        + Convert.ToBase64String(hash) + "\r\n\r\n";
                }
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), deadline.Token);
                var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, TimeSpan.FromSeconds(30));
                if (!upgrade) client.Client.Shutdown(SocketShutdown.Send);
                return new AcceptedSocket(client, socket, lines[0], headers);
            }
            catch { client.Dispose(); throw; }
        }

        public void Dispose()
        {
            Session.Dispose();
            Listener.Stop();
            Directory.Delete(_root, true);
        }
    }

    private sealed class RealtimeRest(MediaServerKind kind) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host, "回环 REST 假传输拒绝真实服务器");
            var path = request.RequestUri.AbsolutePath;
            var json = path.EndsWith("System/Info/Public", StringComparison.Ordinal)
                ? "{\"ServerName\":\"synthetic-realtime\",\"Version\":\"12.2\",\"Id\":\"synthetic-server\",\"ProductName\":\"" + kind + "\"}"
                : path.EndsWith("UserViews", StringComparison.Ordinal) || path.EndsWith("/Views", StringComparison.Ordinal)
                    ? "{\"Items\":[],\"TotalRecordCount\":0}"
                    : "{\"Id\":\"u\",\"Name\":\"fixture\",\"Policy\":{\"IsAdministrator\":true}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
