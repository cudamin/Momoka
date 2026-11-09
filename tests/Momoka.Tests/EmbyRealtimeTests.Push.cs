using System.Net.WebSockets;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class EmbyRealtimeTests
{
    private const string PlexCreated = """{"NotificationContainer":{"type":"timeline","TimelineEntry":[{"identifier":"com.plexapp.plugins.library","itemID":"42","sectionID":1,"state":0}]}}""";

    private static void RegisterPush()
    {
        Test("Plex 实时：原生端点保留反代前缀，URL 无令牌和旧查询，身份只在头部", () =>
        {
            Assert.True(EmbyRealtimeProtocol.Supports(MediaServerKind.Plex));
            var connection = new EmbyConnection(new Uri("https://user:password@plex.invalid/proxy/?X-Plex-Token=old#ignored"),
                "synthetic-token", "u", "fixture", "fixture", DeviceIdentity.Create("device", "1"))
            { ServerKind = MediaServerKind.Plex };
            var handshake = EmbyRealtimeProtocol.Handshake(connection);
            Assert.Equal("wss://plex.invalid/proxy/:/websockets/notifications", handshake.Url.AbsoluteUri);
            Assert.Equal("synthetic-token", handshake.Headers.Single(header => header.Key == "X-Plex-Token").Value);
            Assert.Equal("device", handshake.Headers.Single(header => header.Key == "X-Plex-Client-Identifier").Value);
        });
        Test("Plex 实时：Watch 接收库创建完成删除及库更新，过滤播放进度和畸形消息，离页关闭", () =>
            VerifyPlexPushAsync().WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());
        Test("Jellyfin 实时：回应应用心跳、半周期续发、替换周期与取消收尾", () =>
            VerifyKeepAliveAsync().WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());
        Test("Jellyfin 实时：重连后旧心跳退出，新连接等待自己的请求", () =>
            VerifyKeepAliveReconnectAsync().WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());
        Test("Jellyfin 实时：发送挂起时更新周期不取消写入或启动并行发送", () =>
            VerifyPendingKeepAliveAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
    }

    private static async Task SendTextAsync(WebSocket socket, string text) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task VerifyPlexPushAsync()
    {
        using var fixture = new RealtimeFixture(MediaServerKind.Plex);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        var count = 0;
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watch = new EmbyRealtimeWatch();
        watch.Attach([source], key =>
        {
            Assert.Equal(source.Key, key);
            if (Interlocked.Increment(ref count) == 6) complete.TrySetResult();
        });
        using var accepted = await fixture.AcceptAsync();
        CheckHandshake(accepted, source.CurrentConnection, "synthetic-realtime-old");
        foreach (var ignored in new[]
        {
            "not-json", "[]", "{}", """{"NotificationContainer":42}""",
            """{"NotificationContainer":{"type":"playing","PlaySessionStateNotification":[{"state":"playing"}]}}""",
            """{"NotificationContainer":{"type":"status","StatusNotification":[{"notificationName":"OTHER"}]}}""",
            """{"NotificationContainer":{"type":"timeline","TimelineEntry":{}}}""",
            PlexCreated.Replace("\"state\":0", "\"state\":1", StringComparison.Ordinal),
            PlexCreated.Replace("com.plexapp.plugins.library", "tv.plex.providers.epg", StringComparison.Ordinal),
            PlexCreated.Replace("\"sectionID\":1", "\"sectionID\":0", StringComparison.Ordinal),
            PlexCreated.Replace("\"42\"", "null", StringComparison.Ordinal)
        })
        {
            Assert.False(Momoka.Plex.PlexRealtimeProtocol.IsLibraryChanged(ignored), ignored);
            await SendTextAsync(accepted.Socket, ignored);
        }
        foreach (var valid in new[]
        {
            PlexCreated,
            PlexCreated.Replace("\"state\":0", "\"state\":5", StringComparison.Ordinal),
            PlexCreated.Replace("\"state\":0", "\"state\":9", StringComparison.Ordinal),
            """{"NotificationContainer":{"type":"status","StatusNotification":[{"notificationName":"LIBRARY_UPDATE"}]}}""",
            """{"_elementType":"NotificationContainer","type":"timeline","_children":[{"identifier":"com.plexapp.plugins.library","itemID":42,"sectionID":"1","state":0}]}""",
            """{"_elementType":"NotificationContainer","type":"status","_children":[{"notificationName":"LIBRARY_UPDATE"}]}"""
        })
        {
            Assert.True(Momoka.Plex.PlexRealtimeProtocol.IsLibraryChanged(valid), valid);
            await SendTextAsync(accepted.Socket, valid);
        }
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(6, Volatile.Read(ref count));
        watch.Release();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await accepted.Socket.ReceiveAsync(new byte[64].AsMemory(), deadline.Token); }
        catch (WebSocketException) { }
        Assert.False(deadline.IsCancellationRequested, "离页必须立即关闭实际连接");
        Assert.Equal(6, Volatile.Read(ref count));
    }

    private static async Task VerifyKeepAliveAsync()
    {
        using var fixture = new RealtimeFixture(MediaServerKind.Jellyfin);
        await fixture.RestoreAsync();
        var changes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new EmbyRealtimeConnection(fixture.Session.Capture(), _ => changes.TrySetResult());
        connection.Start();
        using var accepted = await fixture.AcceptAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var first = ReceiveTextAsync(accepted.Socket, deadline.Token);
        foreach (var data in new[] { "null", "0", "-1", "1", "1.5", "3601", "\"60\"", "{}" })
            await SendTextAsync(accepted.Socket, "{\"MessageType\":\"ForceKeepAlive\",\"Data\":" + data + "}");
        await SendTextAsync(accepted.Socket, "{\"MessageType\":\"LibraryChanged\"}");
        await changes.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(first.IsCompleted, "无效心跳不能发送任何回应");
        await SendTextAsync(accepted.Socket, "{\"MessageType\":\"ForceKeepAlive\",\"Data\":2}");
        Assert.Equal("{\"MessageType\":\"KeepAlive\"}", await first.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("{\"MessageType\":\"KeepAlive\"}", await ReceiveTextAsync(accepted.Socket, deadline.Token).WaitAsync(TimeSpan.FromSeconds(2)));
        await SendTextAsync(accepted.Socket, "{\"MessageType\":\"ForceKeepAlive\",\"Data\":60}");
        Assert.Equal("{\"MessageType\":\"KeepAlive\"}", await ReceiveTextAsync(accepted.Socket, deadline.Token));
        var quiet = ReceiveTextAsync(accepted.Socket, deadline.Token);
        await Task.Delay(1400, deadline.Token);
        Assert.False(quiet.IsCompleted, "新周期替换旧周期，不能继续一秒一发");
        connection.Cancel();
        try { await quiet.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (WebSocketException) { }
    }

    private static async Task VerifyKeepAliveReconnectAsync()
    {
        using var fixture = new RealtimeFixture(MediaServerKind.Jellyfin);
        await fixture.RestoreAsync();
        await using var connection = new EmbyRealtimeConnection(fixture.Session.Capture(), _ => { });
        connection.Start();
        using var first = await fixture.AcceptAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        await SendTextAsync(first.Socket, "{\"MessageType\":\"ForceKeepAlive\",\"Data\":2}");
        Assert.Equal("{\"MessageType\":\"KeepAlive\"}", await ReceiveTextAsync(first.Socket, deadline.Token));
        first.Socket.Abort();
        using var second = await fixture.AcceptAsync();
        var next = ReceiveTextAsync(second.Socket, deadline.Token);
        await Task.Delay(1200, deadline.Token);
        Assert.False(next.IsCompleted, "旧连接的周期不能带到新连接");
        await SendTextAsync(second.Socket, "{\"MessageType\":\"ForceKeepAlive\",\"Data\":60}");
        Assert.Equal("{\"MessageType\":\"KeepAlive\"}", await next);
        connection.Cancel();
    }

    private static async Task VerifyPendingKeepAliveAsync()
    {
        using var socket = new BlockedSendSocket();
        await using var heartbeat = new JellyfinRealtimeKeepAlive(socket, CancellationToken.None);
        await heartbeat.ReceiveAsync("{\"MessageType\":\"ForceKeepAlive\",\"Data\":2}");
        await socket.First.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await heartbeat.ReceiveAsync("{\"MessageType\":\"ForceKeepAlive\",\"Data\":60}");
        Assert.Equal(0, socket.Cancellations, "更新周期不得取消尚未完成的网络发送");
        Assert.Equal(1, socket.MaximumConcurrent);
        socket.Release.TrySetResult();
        await socket.Second.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, socket.MaximumConcurrent);
        Assert.Equal(0, socket.Cancellations);
    }

    private sealed class BlockedSendSocket : WebSocket
    {
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Cancellations;
        public int MaximumConcurrent;
        private int _calls;
        private int _active;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() => Release.TrySetResult();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active));
            var call = Interlocked.Increment(ref _calls);
            Assert.Equal("{\"MessageType\":\"KeepAlive\"}", Encoding.UTF8.GetString(buffer));
            try
            {
                if (call == 1)
                {
                    First.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
                else Second.TrySetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref Cancellations);
                throw;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
