using Momoka.Configuration;
using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>所有在线服务器账号及默认管理身份；每个来源独立恢复、撤销与持有凭据。</summary>
public sealed class EmbySession : IDisposable
{
    private const string Category = "session";
    private readonly EmbyHttp _http;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly CredentialVault _vault;
    private readonly object _lifecycleGate = new();
    private readonly Dictionary<string, EmbySessionScope> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _attempts = new(StringComparer.Ordinal);
    private EmbySessionScope? _current;
    private long _selection;
    private bool _disposed;
    private int _pendingConnections;
    private string? _pendingSignedOut;

    public EmbySession(AppSettings settings, SettingsStore store, CredentialVault vault, DeviceIdentity device)
        : this(settings, store, vault, device, null) { }

    internal EmbySession(AppSettings settings, SettingsStore store, CredentialVault vault, DeviceIdentity device,
        HttpMessageHandler? handler)
    {
        _settings = settings;
        _store = store;
        _vault = vault;
        _http = new EmbyHttp(handler);
        Gateway = new EmbyServerGateway(_http, device);
        Device = device;
    }

    /// <summary>仅在最后一个在线来源退出时通知原单会话外壳。</summary>
    public event EventHandler<string>? SignedOut;
    public event EventHandler? SourcesChanged;
    public EmbyServerGateway Gateway { get; }
    public DeviceIdentity Device { get; }
    public ServerProfile? Server { get { lock (_lifecycleGate) return _current?.IdentityState.Server; } }
    public AccountProfile? Account { get { lock (_lifecycleGate) return _current?.IdentityState.Account; } }
    public bool IsSignedIn { get { lock (_lifecycleGate) return _sources.Count > 0; } }
    public EmbyConnection? Connection { get { lock (_lifecycleGate) return _current?.Client.Connection; } }
    public EmbyClient Client => Capture().Client;
    public string ServerDisplayName => Connection?.ServerName is { Length: > 0 } name ? name : Server?.Name ?? "";
    public IReadOnlyList<EmbySessionScope> Sources
    {
        get { lock (_lifecycleGate) return _sources.Values.Select(source => new EmbySessionScope(this, source.IdentityState)).ToArray(); }
    }

    public EmbySessionScope Capture()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _current is { } current ? new EmbySessionScope(this, current.IdentityState)
                : throw new InvalidOperationException("尚未登录媒体服务器");
        }
    }

    /// <summary>条目已带来源时必须使用该来源，失效后绝不回退到另一个账号。</summary>
    public EmbySessionScope Capture(EmbyItem? item)
    {
        if (item?.Source is not { } source) return Capture();
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsAvailable(source)) throw new OperationCanceledException("此媒体所属的服务器账号已退出登录");
            return source;
        }
    }

    public bool SelectSource(EmbySessionScope source)
    {
        lock (_lifecycleGate)
        {
            if (!IsAvailable(source)) return false;
            ++_selection;
            SetCurrent(source);
            Persist();
        }
        NotifySourcesChanged();
        return true;
    }

    private (string Key, long Generation, long Selection) BeginIdentityChange(ServerProfile server, AccountProfile account, bool select)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = EmbySessionScope.CreateKey(server.Id, account.Id);
            var generation = _attempts.GetValueOrDefault(key) + 1;
            _attempts[key] = generation;
            _pendingConnections++;
            return (key, generation, select ? ++_selection : 0);
        }
    }

    private bool CanCommit(string key, long generation) => !_disposed && _attempts.GetValueOrDefault(key) == generation;
    private static bool HasAddress(ServerProfile server, Uri apiBase) =>
        EmbyServerAddress.TryNormalize(server.Url, out var current, out _, server.ServerKind) && current == apiBase;

    public async Task SignInAsync(ServerProfile server, AccountProfile account, string password, string username,
        bool rememberPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = BeginIdentityChange(server, account, true);
        try
        {
            await SignInAttemptAsync(server, account, password, username, rememberPassword, attempt, cancellationToken).ConfigureAwait(false);
        }
        finally { FinishIdentityChange(); }
    }

    private async Task SignInAttemptAsync(ServerProfile server, AccountProfile account, string password, string username,
        bool rememberPassword, (string Key, long Generation, long Selection) attempt, CancellationToken cancellationToken)
    {
        var apiBase = EmbyServerAddress.Normalize(server.Url, server.ServerKind);
        var connection = await Gateway.AuthenticateAsync(apiBase, username, password, cancellationToken,
            server.ServerKind, server.ServerVersion, server.ServerId).ConfigureAwait(false);
        lock (_lifecycleGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanCommit(attempt.Key, attempt.Generation) || !HasAddress(server, apiBase))
                throw new OperationCanceledException("该账号的登录操作已被替代", cancellationToken);
            SaveCredentials(account, connection, password, rememberPassword);
            Adopt(server, account, connection, attempt.Selection);
            Persist();
        }
        NotifySourcesChanged();
    }

    public Task<bool> TryRestoreAsync(ServerProfile server, AccountProfile account, CancellationToken cancellationToken) =>
        TryRestoreAsync(server, account, true, cancellationToken);

    /// <summary>恢复所有允许自动连接的账号，一个来源失败不会中断其他来源。</summary>
    public async Task<int> RestoreAllAsync(CancellationToken cancellationToken)
    {
        var lastServer = _settings.LastServerId;
        var lastAccount = _settings.LastAccountId;
        var accounts = _settings.Servers.SelectMany(server => server.Accounts
            .Where(account => account.AutoConnect != false && (account.HasSavedToken
                || account.AutoConnect == true && account.HasSavedPassword))
            .Select(account => (Server: server, Account: account))).ToArray();
        long selection;
        lock (_lifecycleGate) selection = _selection;
        await Task.WhenAll(accounts.Select(async entry =>
        {
            try { await TryRestoreAsync(entry.Server, entry.Account, false, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (EmbyApiException error) { Log.Warn(Category, "恢复一个来源失败，继续连接其他来源", error); }
        })).ConfigureAwait(false);
        lock (_lifecycleGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_selection == selection && lastServer is not null && lastAccount is not null
                && _sources.TryGetValue(EmbySessionScope.CreateKey(lastServer, lastAccount), out var preferred))
                SetCurrent(preferred);
            Persist();
        }
        NotifySourcesChanged();
        return Sources.Count;
    }

    private async Task<bool> TryRestoreAsync(ServerProfile server, AccountProfile account, bool select, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EmbySessionScope? online;
        lock (_lifecycleGate)
        {
            _sources.TryGetValue(EmbySessionScope.CreateKey(server.Id, account.Id), out online);
            if (online is not null && !HasAddress(server, online.Connection.ApiBase)) online = null;
        }
        if (online is not null) return !select || SelectSource(online);
        var attempt = BeginIdentityChange(server, account, select);
        try { return await RestoreAttemptAsync(server, account, attempt, cancellationToken).ConfigureAwait(false); }
        finally { FinishIdentityChange(); }
    }

    private async Task<bool> RestoreAttemptAsync(ServerProfile server, AccountProfile account,
        (string Key, long Generation, long Selection) attempt, CancellationToken cancellationToken)
    {
        var savedToken = account.ProtectedAccessToken;
        var token = _vault.GetAccessToken(account);
        var userId = account.UserId;
        var username = account.Username;
        if (userId.Length == 0 || (!account.HasSavedPassword && token.Length == 0)) return false;
        if (!EmbyServerAddress.TryNormalize(server.Url, out var apiBase, out _, server.ServerKind)) return false;
        var connection = new EmbyConnection(apiBase, token, userId, username, server.Name, Device)
        {
            ServerKind = server.ServerKind,
            ServerVersion = server.ServerVersion,
            ServerId = server.ServerId
        };
        if (token.Length > 0)
        {
            try
            {
                if (connection.IsJellyfin || connection.IsFnNas)
                {
                    var detected = await Gateway.DetectAsync(server.Url, cancellationToken).ConfigureAwait(false);
                    if (detected.ServerKind != connection.ServerKind || detected.ApiBase != apiBase) return false;
                    if (connection.ServerId is { Length: > 0 } knownId && detected.Id is { Length: > 0 } id && knownId != id) return false;
                    connection = connection with { ServerVersion = detected.Version, ServerId = detected.Id ?? connection.ServerId };
                }
                var candidate = new EmbyClient(_http, connection);
                if (connection.IsJellyfin || connection.IsFnNas)
                {
                    var current = await InvokeAsync(candidate, static (client, ct) => client.GetCurrentUserAsync(ct), cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(current.Id, userId, StringComparison.Ordinal)) return false;
                    connection = connection with { IsAdministrator = current.Policy?.IsAdministrator };
                    candidate = new EmbyClient(_http, connection);
                }
                var views = await InvokeAsync(candidate, static (client, ct) => client.GetViewsAsync(ct), cancellationToken).ConfigureAwait(false);
                connection = await NameServerAsync(apiBase, connection, cancellationToken).ConfigureAwait(false);
                lock (_lifecycleGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!CanCommit(attempt.Key, attempt.Generation) || !HasAddress(server, apiBase)) return false;
                    var source = Adopt(server, account, connection, attempt.Selection);
                    source.IdentityState.RestoredViews = source.Bind(views);
                    Persist();
                }
                NotifySourcesChanged();
                return true;
            }
            catch (EmbyTokenExpiredException)
            {
                lock (_lifecycleGate)
                {
                    if (!CanCommit(attempt.Key, attempt.Generation) || !HasAddress(server, apiBase)) return false;
                    if (account.ProtectedAccessToken == savedToken) _vault.ClearAccessToken(account);
                    Persist();
                }
            }
            catch (EmbyApiException error)
            {
                Log.Warn(Category, "无法使用保存的令牌恢复登录", error);
                return false;
            }
        }
        string password;
        bool remember;
        lock (_lifecycleGate)
        {
            if (!CanCommit(attempt.Key, attempt.Generation) || !HasAddress(server, apiBase) || !account.HasSavedPassword) return false;
            password = _vault.GetPassword(account);
            remember = account.RememberPassword;
        }
        try
        {
            connection = await Gateway.AuthenticateAsync(apiBase, username, password, cancellationToken,
                connection.ServerKind, connection.ServerVersion, connection.ServerId).ConfigureAwait(false);
            lock (_lifecycleGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanCommit(attempt.Key, attempt.Generation) || !HasAddress(server, apiBase)
                    || !string.Equals(connection.UserId, userId, StringComparison.Ordinal)) return false;
                SaveCredentials(account, connection, password, remember);
                Adopt(server, account, connection, attempt.Selection);
                Persist();
            }
            NotifySourcesChanged();
            return true;
        }
        catch (EmbyApiException error)
        {
            Log.Warn(Category, "使用保存的密码自动登录失败", error);
            return false;
        }
    }

    public List<EmbyItem>? TakeRestoredViews(EmbySessionScope? source = null)
    {
        lock (_lifecycleGate)
        {
            source ??= _current;
            if (source is null || !IsAvailable(source)) return null;
            var views = source.IdentityState.RestoredViews;
            source.IdentityState.RestoredViews = null;
            return views;
        }
    }

    public Task<T> ExecuteAsync<T>(Func<EmbyClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        ExecuteWithSourceAsync(Capture(), operation, cancellationToken);

    public Task ExecuteAsync(Func<EmbyClient, CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(async (client, token) => { await operation(client, token).ConfigureAwait(false); return true; }, cancellationToken);

    internal bool IsCurrent(EmbySessionScope scope) { lock (_lifecycleGate) return IsAvailable(scope) && ReferenceEquals(_current?.IdentityState, scope.IdentityState); }
    internal bool IsAvailable(EmbySessionScope scope)
    {
        lock (_lifecycleGate)
            return !_disposed && _sources.TryGetValue(scope.Key, out var source)
                && ReferenceEquals(source.IdentityState, scope.IdentityState) && !scope.Lifetime.IsCancellationRequested;
    }

    internal async Task<T> ExecuteAsync<T>(EmbySessionScope scope, Func<EmbyClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsAvailable(scope)) throw new OperationCanceledException("已退出捕获的登录会话");
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scope.Lifetime);
        var result = await ExecuteWithSourceAsync(scope, operation, lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<T> ExecuteWithSourceAsync<T>(EmbySessionScope scope, Func<EmbyClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var client = scope.Client;
        T result;
        try { result = await InvokeAsync(client, operation, cancellationToken).ConfigureAwait(false); }
        catch (EmbyTokenExpiredException)
        {
            var fresh = await TryReauthenticateAsync(scope, client, cancellationToken).ConfigureAwait(false);
            if (fresh is null) throw;
            cancellationToken.ThrowIfCancellationRequested();
            result = await InvokeAsync(fresh, operation, cancellationToken).ConfigureAwait(false);
        }
        return scope.Bind(result);
    }

    private static async Task<T> InvokeAsync<T>(EmbyClient client, Func<EmbyClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(client, cancellationToken).ConfigureAwait(false); }
        catch (Momoka.FnNas.FnNasApiException error) { throw Momoka.FnNas.FnNasMediaAdapter.Translate(error); }
    }

    private EmbyClient? ReplacementClient(EmbySessionScope scope, EmbyClient stale) =>
        !_disposed && _sources.TryGetValue(scope.Key, out var current) && !ReferenceEquals(current.Client, stale)
            && current.Client.Connection.IsSameIdentityAs(stale.Connection) ? current.Client : null;

    private async Task<EmbyClient?> TryReauthenticateAsync(EmbySessionScope scope, EmbyClient stale, CancellationToken cancellationToken)
    {
        var state = scope.IdentityState;
        await state.Reauthentication.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string password;
            string savedPassword;
            long generation;
            lock (_lifecycleGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAvailable(scope) || !ReferenceEquals(scope.Client, stale)) return ReplacementClient(scope, stale);
                generation = _attempts.GetValueOrDefault(scope.Key);
                savedPassword = state.Account.ProtectedPassword;
                password = _vault.GetPassword(state.Account);
            }
            if (savedPassword.Length == 0)
            {
                EndSource(scope.Key, "登录状态已过期，请重新登录", stale, generation, false);
                return null;
            }
            EmbyConnection connection;
            try
            {
                connection = await Gateway.AuthenticateAsync(stale.Connection.ApiBase, stale.Connection.UserName, password, cancellationToken,
                    stale.Connection.ServerKind, stale.Connection.ServerVersion, stale.Connection.ServerId).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                lock (_lifecycleGate)
                {
                    if (!CanCommit(scope.Key, generation) || !IsAvailable(scope) || !ReferenceEquals(scope.Client, stale))
                        return ReplacementClient(scope, stale);
                }
                if (error is not EmbyApiException) throw;
                Log.Warn(Category, "一个来源自动重新登录失败", error);
                EndSource(scope.Key, "登录状态已过期，请重新登录", stale, generation, false);
                return null;
            }
            lock (_lifecycleGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanCommit(scope.Key, generation) || !IsAvailable(scope) || !ReferenceEquals(scope.Client, stale))
                    return ReplacementClient(scope, stale);
                if (!HasAddress(state.Server, stale.Connection.ApiBase) || state.Account.ProtectedPassword != savedPassword) return null;
                if (connection.IsSameIdentityAs(stale.Connection))
                {
                    SaveCredentials(state.Account, connection, password, state.Account.RememberPassword);
                    state.Client = new EmbyClient(_http, connection);
                    Persist();
                    return state.Client;
                }
            }
            EndSource(scope.Key, "登录状态已过期，请重新登录", stale, generation, false);
            return null;
        }
        finally { state.Reauthentication.Release(); }
    }

    private async Task<EmbyConnection> NameServerAsync(Uri apiBase, EmbyConnection connection, CancellationToken cancellationToken)
    {
        if (connection.IsFnNas) return connection;
        try
        {
            var info = await Gateway.GetPublicSystemInfoAsync(apiBase, cancellationToken).ConfigureAwait(false);
            return connection with
            {
                ServerName = info.ServerName is { Length: > 0 } name ? name : connection.ServerName,
                ServerVersion = info.Version ?? connection.ServerVersion,
                ServerId = info.Id ?? connection.ServerId
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Warn(Category, "无法读取服务器名称，先用本地记住的名字", error);
            return connection;
        }
    }

    private void SaveCredentials(AccountProfile account, EmbyConnection connection, string password, bool remember)
    {
        account.Username = connection.UserName;
        account.UserId = connection.UserId;
        account.LastSignedIn = DateTimeOffset.Now;
        account.AutoConnect = true;
        _vault.SetAccessToken(account, connection.AccessToken);
        _vault.SetPassword(account, password, remember);
    }

    // 所有调用方都持有生命周期锁。一个账号的新令牌共享给已捕获的来源，不改变管理默认。
    private EmbySessionScope Adopt(ServerProfile server, AccountProfile account, EmbyConnection connection, long selection)
    {
        if (connection.ServerName.Length > 0 && server.HasPlaceholderName) server.Name = connection.ServerName;
        server.ServerKind = connection.ServerKind;
        server.ServerVersion = connection.ServerVersion;
        server.ServerId = connection.ServerId;
        account.AutoConnect = true;
        var key = EmbySessionScope.CreateKey(server.Id, account.Id);
        var client = new EmbyClient(_http, connection);
        if (_sources.TryGetValue(key, out var existing) && existing.Client.Connection.IsSameIdentityAs(connection))
        {
            existing.IdentityState.Client = client;
            existing.IdentityState.RestoredViews = null;
        }
        else
        {
            if (existing is not null) CancelLifetime(existing.IdentityState.Lifetime);
            existing = new EmbySessionScope(this, new EmbySessionScope.Identity(client, server, account));
            _sources[key] = existing;
        }
        if (_current is null || selection != 0 && selection == _selection) SetCurrent(existing);
        return existing;
    }

    private void SetCurrent(EmbySessionScope source)
    {
        _current = _sources[source.Key];
        _settings.Remember(source.IdentityState.Server, source.IdentityState.Account);
    }

    public void SignOut()
    {
        string? key;
        lock (_lifecycleGate)
        {
            key = _current?.Key;
            if (key is null)
            {
                foreach (var pending in _attempts.Keys.ToArray()) _attempts[pending]++;
                _selection++;
                return;
            }
        }
        EndSource(key, "已退出登录");
    }

    public void SignOut(ServerProfile server, AccountProfile account) =>
        EndSource(EmbySessionScope.CreateKey(server.Id, account.Id), "已退出登录", account: account);

    public void SignOutServer(ServerProfile server)
    {
        foreach (var account in server.Accounts.ToArray()) SignOut(server, account);
    }

    private void EndSource(string key, string reason, EmbyClient? expected = null, long generation = 0,
        bool explicitSignOut = true, AccountProfile? account = null)
    {
        EmbySessionScope? removed;
        bool last;
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _sources.TryGetValue(key, out removed);
            if (expected is not null && (!CanCommit(key, generation) || !ReferenceEquals(removed?.Client, expected))) return;
            _attempts[key] = _attempts.GetValueOrDefault(key) + 1;
            account ??= removed?.IdentityState.Account;
            if (account is not null)
            {
                _vault.ClearAccessToken(account);
                if (explicitSignOut) account.AutoConnect = false;
            }
            _sources.Remove(key);
            if (ReferenceEquals(_current, removed) && removed is not null)
            {
                _current = _sources.Values.FirstOrDefault();
                if (_current is not null) SetCurrent(_current);
            }
            last = removed is not null && _sources.Count == 0;
            if (last && _pendingConnections > 0)
            {
                _pendingSignedOut = reason;
                last = false;
            }
            Persist();
        }
        if (removed is not null) CancelLifetime(removed.IdentityState.Lifetime);
        NotifySourcesChanged();
        if (last) NotifySignedOut(reason);
    }

    private void FinishIdentityChange()
    {
        string? reason;
        lock (_lifecycleGate)
        {
            _pendingConnections--;
            reason = _pendingConnections == 0 && _sources.Count == 0 ? _pendingSignedOut : null;
            if (_pendingConnections == 0) _pendingSignedOut = null;
        }
        if (reason is not null) NotifySignedOut(reason);
    }

    private void NotifySourcesChanged()
    {
        if (SourcesChanged is not { } changed) return;
        foreach (EventHandler subscriber in changed.GetInvocationList())
        {
            try { subscriber(this, EventArgs.Empty); }
            catch (Exception error) { Log.Warn(Category, "来源变更通知失败", error); }
        }
    }

    private void NotifySignedOut(string reason)
    {
        if (SignedOut is not { } signedOut) return;
        foreach (EventHandler<string> subscriber in signedOut.GetInvocationList())
        {
            try { subscriber(this, reason); }
            catch (Exception error) { Log.Warn(Category, "退出登录通知失败", error); }
        }
    }

    private static void CancelLifetime(CancellationTokenSource lifetime)
    {
        try { lifetime.Cancel(); }
        catch (AggregateException error) { Log.Warn(Category, "撤销来源时回调失败", error); }
        finally { lifetime.Dispose(); }
    }

    private void Persist()
    {
        try { _store.Save(_settings); }
        catch (Exception error) { Log.Warn(Category, "保存登录设置失败", error); }
    }

    public void Dispose()
    {
        EmbySessionScope[] sources;
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            sources = _sources.Values.ToArray();
            _sources.Clear();
            _current = null;
        }
        try { foreach (var source in sources) CancelLifetime(source.IdentityState.Lifetime); }
        finally { _http.Dispose(); }
    }
}
