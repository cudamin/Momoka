using System.Net;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    private readonly SemaphoreSlim _connectionCheck = new(1, 1);
    private SessionIdentity? _availableIdentity;
    private long _connectionGeneration;
    private long _availabilityRevision;
    private (bool Enabled, string Address, string Username, string ProtectedPassword) _configuration;

    private (bool, string, string, string) ReadConfiguration()
    {
        var current = _settings.MoviePilot;
        var address = MoviePilotAddress.TryNormalize(current.Url, out var normalized, out _)
            ? normalized?.AbsoluteUri ?? "" : current.Url;
        return (current.Enabled, address, current.Username?.Trim() ?? "", current.ProtectedPassword);
    }

    /// <summary>当前配置已启用，且本进程已成功连接；历史测试时间和缓存令牌都不算连接证据。</summary>
    public bool Available
    {
        get
        {
            lock (_sessionGate)
                return Enabled && _availableIdentity is { } identity && MatchesConfiguration(identity);
        }
    }

    public event EventHandler? AvailabilityChanged;

    private bool MatchesConfiguration(SessionIdentity identity) =>
        identity.Generation == Interlocked.Read(ref _connectionGeneration)
        && MoviePilotAddress.TryNormalize(_settings.MoviePilot.Url, out var address, out _) && address == identity.ApiBase
        && identity.Username == (_settings.MoviePilot.Username?.Trim() ?? "")
        && identity.ProtectedPassword == _settings.MoviePilot.ProtectedPassword;

    private void OnConnectionChanged(object? sender, EventArgs args)
    {
        lock (_sessionGate)
        {
            var configuration = ReadConfiguration();
            if (_configuration == configuration) return;
            _configuration = configuration;
            _connectionGeneration++;
            _availabilityRevision++;
            _availableIdentity = null;
            _session = null;
        }
        // 配置变化即使发生在未连接状态，也要通知监测方重新检查。
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetAvailable(SessionIdentity identity, long revision, bool available)
    {
        bool changed;
        lock (_sessionGate)
        {
            if (revision != _availabilityRevision || !MatchesConfiguration(identity)) return;
            changed = (_availableIdentity is not null) != (available && Enabled);
            _availableIdentity = available && Enabled ? identity : null;
            if (!available) _availabilityRevision++;
        }
        if (changed) AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>只读连通性检查；调用方控制重试周期。关闭或配置不全时不发请求。</summary>
    public async Task<bool> RefreshAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled) return false;
        await _connectionCheck.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Enabled) return false;
            SessionIdentity identity;
            try { identity = CaptureIdentity(); }
            catch (MoviePilotException) { return false; }
            var revision = Interlocked.Read(ref _availabilityRevision);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await CallAsync((address, token) => _client.GetAsync(address, token, "dashboard/system", timeout.Token),
                    timeout.Token, identity).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is MoviePilotException or OperationCanceledException)
            {
                SetAvailable(identity, revision, false);
            }
            return Available;
        }
        finally { _connectionCheck.Release(); }
    }

    /// <summary>设置页显式测试也更新同一个连接状态，但不替用户打开开关。</summary>
    public async Task<MoviePilotStatus> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        var revision = Interlocked.Read(ref _availabilityRevision);
        try
        {
            var status = await new MoviePilotProbe(_client).RunAsync(identity.ApiBase, identity.Username,
                _credentials.GetPassword(identity.ProtectedPassword), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesConfiguration(identity)) throw new OperationCanceledException("MoviePilot 配置已改变，请重新测试");
            SetAvailable(identity, revision, true);
            return status;
        }
        catch (Exception error) when (error is MoviePilotException)
        {
            SetAvailable(identity, revision, false);
            throw;
        }
    }

    internal static bool ConnectionFailed(Exception error) => error is MoviePilotUnreachableException
        || error is MoviePilotException { Status: HttpStatusCode.Unauthorized }
        || error is MoviePilotException { Status: >= HttpStatusCode.InternalServerError };
}
