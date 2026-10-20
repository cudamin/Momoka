using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>固定服务器账号身份；更换默认管理账号不影响它，退出该账号才撤销。</summary>
public sealed class EmbySessionScope
{
    private readonly EmbySession _session;

    internal EmbySessionScope(EmbySession session, Identity identity)
    {
        _session = session;
        IdentityState = identity;
        Connection = identity.Client.Connection;
        Lifetime = identity.Lifetime.Token;
    }

    /// <summary>捕获时的连接快照；新请求通过 ExecuteAsync 取得该身份的最新令牌。</summary>
    public EmbyConnection Connection { get; }
    public string Key => CreateKey(ServerProfileId, AccountProfileId);
    public string ServerProfileId => IdentityState.Server.Id;
    public string AccountProfileId => IdentityState.Account.Id;
    public string ServerName => IdentityState.Server.Name;
    public string DisplayName => $"{ServerName} · {IdentityState.Account.Username}";
    public bool IsCurrent => _session.IsCurrent(this);
    public bool IsAvailable => _session.IsAvailable(this);

    public void ThrowIfNotCurrent()
    {
        if (!IsCurrent) throw new OperationCanceledException("任务所属的默认登录身份已改变");
    }

    public void ThrowIfNotAvailable()
    {
        if (!IsAvailable) throw new OperationCanceledException("任务所属的服务器账号已退出登录");
    }

    public void ThrowIfUnavailable() => ThrowIfNotAvailable();

    public EmbyItem Bind(EmbyItem item)
    {
        item.Source = this;
        return item;
    }

    public T Bind<T>(T result)
    {
        if (result is EmbyItem item) Bind(item);
        else if (result is ItemsResult query) Bind(query.Items);
        else if (result is MovieRecommendation recommendation) Bind(recommendation.Items);
        else if (result is IEnumerable<MovieRecommendation> recommendations)
            foreach (var group in recommendations) Bind(group.Items);
        else if (result is IEnumerable<EmbyItem> items)
            foreach (var entry in items) Bind(entry);
        return result;
    }

    internal EmbyClient Client => IdentityState.Client;
    internal Identity IdentityState { get; }
    internal CancellationToken Lifetime { get; }
    internal static string CreateKey(string serverId, string accountId) => $"{serverId.Length}:{serverId}{accountId}";

    internal sealed class Identity(EmbyClient client, ServerProfile server, AccountProfile account)
    {
        public EmbyClient Client { get; set; } = client;
        public ServerProfile Server { get; } = server;
        public AccountProfile Account { get; } = account;
        public CancellationTokenSource Lifetime { get; } = new();
        public SemaphoreSlim Reauthentication { get; } = new(1, 1);
        public SemaphoreSlim PasswordChanges { get; } = new(1, 1);
        public List<EmbyItem>? RestoredViews { get; set; }
    }

    public Task<T> ExecuteAsync<T>(Func<EmbyClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        _session.ExecuteAsync(this, operation, cancellationToken);

    public Task ExecuteAsync(Func<EmbyClient, CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(async (client, token) => { await operation(client, token).ConfigureAwait(false); return true; }, cancellationToken);
}
