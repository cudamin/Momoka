using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class EmbyRealtimeTests
{
    // The supported fast path consumes only MessageType. 1 MiB is deliberately far above its fixtures.
    private const int MessageBudget = 1 << 20;

    internal static void RegisterBudgets()
    {
        RegisterMessageBoundaries();
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        foreach (var type in new[] { WebSocketMessageType.Text, WebSocketMessageType.Binary })
        foreach (var end in new[] { true, false })
        {
            var product = kind;
            var messageType = type;
            var endOfMessage = end;
            Test($"PERF-11：{kind} {type} 累计1MiB+1字节（结束={end}）中止且沿既有退避重连", () =>
                VerifyOverBudgetAsync(product, messageType, endOfMessage).WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult());
        }
    }

    private static async Task VerifyOverBudgetAsync(MediaServerKind kind, WebSocketMessageType type, bool end)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        var notifications = 0;
        var changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new EmbyRealtimeConnection(source, key =>
        {
            Interlocked.Increment(ref notifications);
            changed.TrySetResult(key);
        });
        connection.Start();
        using var first = await fixture.AcceptAsync();
        var started = System.Diagnostics.Stopwatch.StartNew();
        await SendFragmentsAsync(first.Socket, PaddedEvent(MessageBudget), type, 8192, end: false);
        await first.Socket.SendAsync(new byte[] { (byte)' ' }.AsMemory(), type, end, CancellationToken.None);
        await ExpectDisconnectedAsync(first.Socket);
        Assert.Equal(0, Volatile.Read(ref notifications), "超限或未结束消息不能通知媒体库");
        using var next = await fixture.AcceptAsync();
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(800), "不绕过既有约1秒退避");
        await next.Socket.SendAsync(Encoding.UTF8.GetBytes("{\"MessageType\":\"LibraryChanged\"}").AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal(source.Key, await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    private static async Task ExpectDisconnectedAsync(WebSocket socket)
    {
        var disconnected = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var result = await socket.ReceiveAsync(new byte[16], timeout.Token);
            disconnected = result.MessageType == WebSocketMessageType.Close;
        }
        catch (WebSocketException)
        {
            disconnected = true;
        }
        catch (OperationCanceledException)
        {
            throw new AssertionException("整条消息超限后连接仍在接收，未立即中止");
        }
        Assert.True(disconnected, "收到非关闭数据不是预算中止证据");
    }
}
