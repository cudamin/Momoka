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

    /// <summary>该在线身份当前确认的权限；不使用捕获时的令牌/播放连接快照，撤销后返回未知。</summary>
    public bool? IsAdministrator => _session.CurrentPermissions(this).IsAdministrator;

    /// <summary>同身份客户端权限快照的代次；旧能力读取与页面操作不能跨新快照回填。</summary>
    public long PermissionRevision => _session.CurrentPermissions(this).Revision;

    /// <summary>同一在线身份的作用域共享此所有者；退出再登录后使用新的所有者。</summary>
    public object UserStateOwner => IdentityState.UserStateOwner;

    /// <summary>Marks reads against local confirmed edits, without turning a saved DTO into a cache.</summary>
    public long UserStateRevision => Interlocked.Read(ref IdentityState.UserStateRevision);
    public long ConfirmUserStateChange() => Interlocked.Increment(ref IdentityState.UserStateRevision);

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
        if (item.UserStateReadRevision < 0) item.UserStateReadRevision = UserStateRevision;
        return item;
    }

    public T Bind<T>(T result) => Bind(result, UserStateRevision);

    internal T Bind<T>(T result, long readRevision)
    {
        void BindRead(EmbyItem item)
        {
            item.Source = this;
            item.UserStateReadRevision = readRevision;
        }
        if (result is EmbyItem item) BindRead(item);
        else if (result is ItemsResult query) Bind(query.Items, readRevision);
        else if (result is MovieRecommendation recommendation) Bind(recommendation.Items, readRevision);
        else if (result is IEnumerable<MovieRecommendation> recommendations)
            foreach (var group in recommendations) Bind(group.Items, readRevision);
        else if (result is IEnumerable<EmbyItem> items)
            foreach (var entry in items) BindRead(entry);
        return result;
    }

    internal EmbyConnection CurrentConnection => _session.CurrentConnection(this);
    internal EmbyClient Client => IdentityState.Client;
    internal Identity IdentityState { get; }
    internal CancellationToken Lifetime { get; }
    internal static string CreateKey(string serverId, string accountId) => $"{serverId.Length}:{serverId}{accountId}";

    internal sealed class Identity(EmbyClient client, ServerProfile server, AccountProfile account)
    {
        public object UserStateOwner { get; } = new();
        public long UserStateRevision;
        public EmbyClient Client { get; set; } = client;
        public long PermissionRevision { get; set; }
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
