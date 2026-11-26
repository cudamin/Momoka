using System.Net;

namespace Momoka.Scrobbling;

public sealed partial class ScrobblingService
{
    private int _bangumiConnection;
    private int _doubanConnection;
    private long _connectionRevision;
    private readonly object _connectionGate = new();

    public ScrobblingConnectionState BangumiConnection => (ScrobblingConnectionState)Volatile.Read(ref _bangumiConnection);
    public ScrobblingConnectionState DoubanConnection => (ScrobblingConnectionState)Volatile.Read(ref _doubanConnection);

    private void ResetConnections()
    {
        lock (_connectionGate)
        {
            _connectionRevision++;
            Volatile.Write(ref _bangumiConnection, (int)ScrobblingConnectionState.Unchecked);
            Volatile.Write(ref _doubanConnection, (int)ScrobblingConnectionState.Unchecked);
        }
    }

    /// <summary>Read-only checks owned by the settings visit and captured configuration. Never changes a collection.</summary>
    public async Task CheckConnectionsAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        var snapshot = Volatile.Read(ref _snapshot);
        long revision;
        lock (_connectionGate) revision = ++_connectionRevision;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, snapshot.Cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        bool Current() => !_disposed && revision == Volatile.Read(ref _connectionRevision)
            && ReferenceEquals(snapshot, Volatile.Read(ref _snapshot));
        void Publish(ScrobblingProvider provider, ScrobblingConnectionState state)
        {
            lock (_connectionGate)
            {
                if (!Current()) return;
                if (provider == ScrobblingProvider.Bangumi) Volatile.Write(ref _bangumiConnection, (int)state);
                else Volatile.Write(ref _doubanConnection, (int)state);
            }
            try { Changed?.Invoke(); } catch (Exception) { /* Optional presentation only. */ }
        }
        async Task CheckAsync(ScrobblingProvider provider, bool enabled)
        {
            if (!enabled) return;
            if (provider == ScrobblingProvider.Bangumi && snapshot.Token.Length == 0)
            {
                Publish(provider, ScrobblingConnectionState.MissingCredential);
                return;
            }
            Publish(provider, ScrobblingConnectionState.Checking);
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                var connected = true;
                if (provider == ScrobblingProvider.Bangumi)
                    await _bangumi.CheckAccessAsync(_protector.Unprotect(snapshot.Token), deadline.Token).ConfigureAwait(false);
                else connected = await _douban.CheckLoginAsync(deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                Publish(provider, connected ? ScrobblingConnectionState.Connected : ScrobblingConnectionState.SignedOut);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || snapshot.Cancellation.IsCancellationRequested)
            {
                Publish(provider, ScrobblingConnectionState.Unchecked);
            }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Publish(provider, ScrobblingConnectionState.SignedOut);
            }
            catch (Exception) { Publish(provider, ScrobblingConnectionState.Failed); }
        }
        await Task.WhenAll(CheckAsync(ScrobblingProvider.Bangumi, snapshot.Bangumi),
            CheckAsync(ScrobblingProvider.Douban, snapshot.Douban)).ConfigureAwait(false);
    }
}
