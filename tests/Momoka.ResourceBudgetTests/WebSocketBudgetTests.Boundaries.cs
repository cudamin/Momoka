using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class EmbyRealtimeTests
{
    private static void RegisterMessageBoundaries()
    {
        Test("PERF-11字节边界：UTF8三/四字节字符逐字节碎片解码保持原文，不是逐片GetString", () =>
        {
            const string original = "{\"Data\":\"中🌸文é𠀀\",\"MessageType\":\"LibraryChanged\"}";
            var bytes = Encoding.UTF8.GetBytes(original);
            var frame = new EmbyRealtimeMessage(WebSocketMessageType.Text);
            var chars = new char[Encoding.UTF8.GetMaxCharCount(1)];
            for (var index = 0; index < bytes.Length; index++)
                Assert.True(frame.TryAppend(bytes.AsSpan(index, 1), chars, index == bytes.Length - 1));
            Assert.Equal(bytes.Length, frame.Bytes);
            Assert.Equal(original, frame.Text);
            Assert.DoesNotContain("�", frame.Text);
            Assert.Equal("LibraryChanged", EmbyRealtimeProtocol.ReadMessageType(frame.Text));
        });

        foreach (var type in new[] { WebSocketMessageType.Text, WebSocketMessageType.Binary })
        {
            var messageType = type;
            Test($"PERF-11字节边界：{type} 恰好1MiB可收，超出的无效UTF8在decode/追加前拒绝", () =>
            {
                var frame = new EmbyRealtimeMessage(messageType);
                var block = new byte[16384];
                Array.Fill(block, (byte)' ');
                var chars = new char[Encoding.UTF8.GetMaxCharCount(block.Length)];
                for (var received = 0; received < MessageBudget; received += block.Length)
                    Assert.True(frame.TryAppend(block, chars, false));
                Assert.Equal(MessageBudget, frame.Bytes);
                var before = GC.GetAllocatedBytesForCurrentThread();
                Assert.False(frame.TryAppend(new byte[] { 0xFF }, chars, true));
                Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1024,
                    "超预算字节不能引发整条文本字符串或builder扩张");
                Assert.Equal(MessageBudget, frame.Bytes);
                if (messageType == WebSocketMessageType.Text) Assert.Equal(MessageBudget, frame.Text!.Length);
                else Assert.Null(frame.Text, "二进制消息不分配整条builder或文本");
            });
        }

        Test("PERF-11字节边界：非ASCII按UTF8 bytes而非字符数计预算，未结束四字节前缀取消不flush", () =>
        {
            var frame = new EmbyRealtimeMessage(WebSocketMessageType.Text);
            var block = Encoding.UTF8.GetBytes(new string('中', 4096));
            var chars = new char[Encoding.UTF8.GetMaxCharCount(block.Length)];
            var received = 0;
            while (received + block.Length <= MessageBudget)
            {
                Assert.True(frame.TryAppend(block, chars, false));
                received += block.Length;
            }
            Assert.True(frame.TryAppend(new byte[MessageBudget - received], chars, false));
            Assert.False(frame.TryAppend(Encoding.UTF8.GetBytes("中"), chars, true));
            Assert.Equal(MessageBudget, frame.Bytes);
            var prefix = new EmbyRealtimeMessage(WebSocketMessageType.Text);
            Assert.True(prefix.TryAppend(new byte[] { 0xF0, 0x9F }, chars, false));
            Assert.Equal("", prefix.Text);
        });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            var product = kind;
            foreach (var length in new[] { MessageBudget - 1, MessageBudget })
            {
                var size = length;
                Test($"PERF-11真实回环：{kind} 合法消息{length} UTF8字节与下一条均可正常通知", () =>
                    VerifyMaximumAsync(product, size).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
            }
            Test($"PERF-11真实回环：{kind} normal逐字节Unicode碎片与binary/Sessions/空帧过滤", () =>
                VerifyFragmentedUnicodeAsync(product).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
            Test($"PERF-11真实回环：{kind} 多条总计超过1MiB不串用上一条预算", () =>
                VerifyMessageResetAsync(product).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
            foreach (var text in new[] { true, false })
            {
                var isText = text;
                Test($"PERF-11真实回环：{kind} 未结束{(text ? "text" : "binary")}达到上限后取消立即释放且不通知", () =>
                    VerifyReceiveCancellationAsync(product, isText).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
            }
            Test($"PERF-11真实回环：{kind} 含LibraryChanged的未结束消息遇EOF只重连不通知", () =>
                VerifyPartialEofAsync(product).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());
        }
    }

    private static async Task VerifyMaximumAsync(MediaServerKind kind, int bytes)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        var notifications = 0;
        var firstChanged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondChanged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = fixture.Session.Capture();
        await using var connection = new EmbyRealtimeConnection(source, key =>
        {
            if (Interlocked.Increment(ref notifications) == 1) firstChanged.TrySetResult(key);
            else secondChanged.TrySetResult(key);
        });
        connection.Start();
        using var server = await fixture.AcceptAsync();
        var message = PaddedEvent(bytes);
        await SendFragmentsAsync(server.Socket, message, WebSocketMessageType.Text, 8192);
        Assert.Equal(source.Key, await firstChanged.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await server.Socket.SendAsync(Encoding.UTF8.GetBytes("{\"MessageType\":\"LibraryChanged\"}").AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal(source.Key, await secondChanged.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, Volatile.Read(ref notifications));
        Console.WriteLine($"    回环正常上界 product={kind} utf8Bytes={bytes} notifications=2");
    }

    private static async Task VerifyFragmentedUnicodeAsync(MediaServerKind kind)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        var notifications = 0;
        var complete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new EmbyRealtimeConnection(source, key =>
        {
            if (Interlocked.Increment(ref notifications) == 2) complete.TrySetResult(key);
        });
        connection.Start();
        using var server = await fixture.AcceptAsync();
        var eventBytes = Encoding.UTF8.GetBytes("{\"Data\":\"中🌸文é𠀀\",\"MessageType\":\"LibraryChanged\"}");
        await SendFragmentsAsync(server.Socket, eventBytes, WebSocketMessageType.Text, 1);
        await server.Socket.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Text, true, CancellationToken.None);
        await SendFragmentsAsync(server.Socket, eventBytes, WebSocketMessageType.Binary, 1);
        await server.Socket.SendAsync(Encoding.UTF8.GetBytes("{\"MessageType\":\"Sessions\"}").AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None);
        await SendFragmentsAsync(server.Socket, eventBytes, WebSocketMessageType.Text, 1);
        Assert.Equal(source.Key, await complete.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, Volatile.Read(ref notifications));
    }

    private static async Task VerifyMessageResetAsync(MediaServerKind kind)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        var notifications = 0;
        var complete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new EmbyRealtimeConnection(source, key =>
        {
            if (Interlocked.Increment(ref notifications) == 10) complete.TrySetResult(key);
        });
        connection.Start();
        using var server = await fixture.AcceptAsync();
        var message = PaddedEvent(128 * 1024);
        for (var index = 0; index < 10; index++)
            await SendFragmentsAsync(server.Socket, message, WebSocketMessageType.Text, 8192);
        Assert.Equal(source.Key, await complete.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(10, Volatile.Read(ref notifications));
    }

    private static async Task VerifyReceiveCancellationAsync(MediaServerKind kind, bool text)
    {
        using var fixture = new RealtimeFixture(kind);
        await fixture.RestoreAsync();
        var source = fixture.Session.Capture();
        var notifications = 0;
        await using var connection = new EmbyRealtimeConnection(source, _ => Interlocked.Increment(ref notifications));
        connection.Start();
        using var server = await fixture.AcceptAsync();
        var message = PaddedEvent(MessageBudget);
        await SendFragmentsAsync(server.Socket, message, text ? WebSocketMessageType.Text : WebSocketMessageType.Binary,
            8192, end: false);
        connection.Cancel();
        await RealtimeLoop(connection).WaitAsync(TimeSpan.FromSeconds(2));
        await connection.DisposeAsync();
        Assert.Equal(0, Volatile.Read(ref notifications));
        await ExpectDisconnectedAsync(server.Socket);
        Assert.False(fixture.Listener.Pending(), "接收取消不能排队下一轮握手");
    }

    private static async Task VerifyPartialEofAsync(MediaServerKind kind)
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
        using var server = await fixture.AcceptAsync();
        await SendFragmentsAsync(server.Socket, PaddedEvent(4096), WebSocketMessageType.Text, 64, end: false);
        server.Socket.Abort(); // Actual EOF; not an artificial cancellation result or copied receive loop.
        using var next = await fixture.AcceptAsync();
        Assert.Equal(0, Volatile.Read(ref notifications));
        await next.Socket.SendAsync(Encoding.UTF8.GetBytes("{\"MessageType\":\"LibraryChanged\"}").AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal(source.Key, await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    private static Task RealtimeLoop(EmbyRealtimeConnection connection) => (Task)typeof(EmbyRealtimeConnection)
        .GetField("_loop", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;

    private static byte[] PaddedEvent(int bytes)
    {
        var prefix = Encoding.UTF8.GetBytes("{\"Data\":{\"ItemsAdded\":[\"合成🌸\"]},\"MessageType\":\"LibraryChanged\"}");
        var result = new byte[bytes];
        Array.Fill(result, (byte)' ');
        prefix.CopyTo(result, 0);
        return result;
    }

    private static async Task SendFragmentsAsync(WebSocket socket, byte[] bytes, WebSocketMessageType type,
        int fragmentBytes, bool end = true)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        for (var sent = 0; sent < bytes.Length;)
        {
            var count = Math.Min(fragmentBytes, bytes.Length - sent);
            var begin = sent;
            sent += count;
            await socket.SendAsync(bytes.AsMemory(begin, count), type, end && sent == bytes.Length, timeout.Token);
        }
    }
}
