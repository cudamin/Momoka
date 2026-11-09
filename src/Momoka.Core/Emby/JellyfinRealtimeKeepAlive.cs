using System.Net.WebSockets;
using System.Text.Json;

namespace Momoka.Emby;

/// <summary>One socket's application heartbeat. A WebSocket ping does not update Jellyfin's session keepalive.</summary>
internal sealed class JellyfinRealtimeKeepAlive(WebSocket socket, CancellationToken lifetime) : IAsyncDisposable
{
    private readonly CancellationTokenSource _sending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _intervalMilliseconds;
    private bool _started;
    private Task _loop = Task.CompletedTask;

    internal Task ReceiveAsync(string message)
    {
        int seconds;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("MessageType", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != "ForceKeepAlive"
                || !root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Number
                || !data.TryGetInt32(out seconds) || seconds is < 2 or > 3600) return Task.CompletedTask;
        }
        catch (JsonException) { return Task.CompletedTask; }

        lifetime.ThrowIfCancellationRequested();
        Volatile.Write(ref _intervalMilliseconds, seconds * 500);
        // Updating a period must never cancel an in-flight SendAsync: ManagedWebSocket treats that
        // cancellation as Abort. Keep one sender, and coalesce new requests into one wake-up.
        if (!_started)
        {
            _started = true;
            _loop = SendAsync(_sending.Token);
        }
        else if (_wake.CurrentCount == 0) _wake.Release();
        return Task.CompletedTask;
    }

    private async Task SendAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await socket.SendAsync("{\"MessageType\":\"KeepAlive\"}"u8.ToArray().AsMemory(),
                    WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                await _wake.WaitAsync(Volatile.Read(ref _intervalMilliseconds), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            // A failed sender must wake the receive loop so the existing reconnect policy can run.
            socket.Abort();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sending.Cancel();
        try { await _loop.ConfigureAwait(false); }
        finally
        {
            _sending.Dispose();
            _wake.Dispose();
        }
    }
}
