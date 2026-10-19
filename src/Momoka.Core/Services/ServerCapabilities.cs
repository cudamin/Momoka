using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Services;

/// <summary>
/// What the server can do, asked once and remembered. Today that is its version, which the sort menu
/// needs because one key exists from 4.7.3 onwards, and whether the signed-in account may administer it;
/// anything else the client has to ask permission for belongs here too rather than in a page.
/// </summary>
public interface IServerCapabilities
{
    MediaServerKind ServerKind => MediaServerKind.Emby;
    bool IsConnected => true;

    /// <summary>
    /// 主界面现在是哪一套服务端界面。只有一台服务器在线时按那台的产品定，没有或两台以上是通用界面
    /// （同一台服务器的多个账号仍算一台）。设置页据此决定服务端管理入口在不在，外壳据此告诉用户当前界面。
    /// </summary>
    ServerInterfaceFlavor InterfaceFlavor => ServerAccess.InterfaceFor(ServerKind);
    /// <summary>
    /// The server's version, once <see cref="ProbeAsync"/> has answered, and null until then.
    /// </summary>
    Version? ServerVersion { get; }

    /// <summary>
    /// Whether the signed-in account may administer this server. Null means unknown. Jellyfin
    /// administration requires a confirmed true value; unknown permission must not expose its controls.
    /// </summary>
    bool? IsAdministrator { get; }

    /// <summary>
    /// 服务器插件贡献的管理页（Emby 主菜单里的 Cinema Intros、DLNA，Jellyfin 的插件配置页）。产品或连接
    /// 模式不适用的时候给空表；失败也静默，设置左栏保留固定条目。默认实现给假实现和旧测试留一条空路。
    /// </summary>
    Task<IReadOnlyList<ServerSettingsPluginPage>> GetAdminMenuPagesAsync(CancellationToken token = default) =>
        Task.FromResult<IReadOnlyList<ServerSettingsPluginPage>>([]);

    /// <summary>
    /// 当前同时在线的每一条服务器身份。设置左栏在多服务器时按服务器分组，正是从这里取组分母；
    /// 每条都带着自己的捕获作用域，所以入口可以绑定到那一台服务器，而不是「当前来源」。
    /// 默认给假实现和旧测试一条空路。
    /// </summary>
    IReadOnlyList<ServerSettingsSource> Sources => [];

    /// <summary>某一台服务器贡献的插件页。默认空表；只有 Emby／Jellyfin 会答出内容。</summary>
    Task<IReadOnlyList<ServerSettingsPluginPage>> GetAdminMenuPagesAsync(string sourceKey, CancellationToken token = default) =>
        Task.FromResult<IReadOnlyList<ServerSettingsPluginPage>>([]);

    Task ProbeAsync(CancellationToken token = default);
}

/// <summary>
/// 设置左栏分组用的一条在线服务器身份。作用域由会话捕获，页面可以用它向指定服务器发请求，
/// 不必先把「当前来源」切过去。
/// </summary>
public sealed record ServerSettingsSource(EmbySessionScope Scope, MediaServerKind Kind, bool IsAdministrator)
{
    public string Key => Scope.Key;
    public string ServerProfileId => Scope.ServerProfileId;
    public string AccountProfileId => Scope.AccountProfileId;
    public string ServerName => Scope.Connection.ServerName is { Length: > 0 } name ? name : Scope.ServerName;
    public string AccountName => Scope.Connection.UserName;
    public bool IsCurrent => Scope.IsCurrent;
    public bool IsAvailable => Scope.IsAvailable;

    /// <summary>左栏组名：产品名在前，同名服务器（比如两台都叫「果服」）因此还分得开。</summary>
    public string Label => ServerAccess.KindName(Kind) + " · " + ServerName;
}

/// <inheritdoc cref="IServerCapabilities"/>
public sealed class ServerCapabilities(EmbySession session) : IServerCapabilities
{
    public MediaServerKind ServerKind => session.Connection?.ServerKind ?? MediaServerKind.Emby;
    public bool IsConnected => session.IsSignedIn;

    public ServerInterfaceFlavor InterfaceFlavor
    {
        get
        {
            var sources = session.Sources;
            return ServerAccess.InterfaceFor(
                sources.Select(source => source.ServerProfileId).Distinct(StringComparer.Ordinal).Count(),
                sources.Select(source => source.Connection.ServerKind));
        }
    }

    private const string Category = "app";

    private readonly object _gate = new();
    private EmbySessionScope? _scope;
    private Version? _serverVersion;
    private bool? _administrator;
    private long _generation;

    /// <inheritdoc />
    public Version? ServerVersion
    {
        get
        {
            EmbySessionScope? scope;
            Version? version;
            lock (_gate)
            {
                scope = _scope;
                version = _serverVersion;
            }
            return scope?.IsCurrent == true ? version
                : Version.TryParse(session.Connection?.ServerVersion, out var saved) ? saved : null;
        }
    }

    /// <inheritdoc />
    public bool? IsAdministrator
    {
        get
        {
            EmbySessionScope? scope;
            bool? administrator;
            lock (_gate)
            {
                scope = _scope;
                administrator = _administrator;
            }
            return scope?.IsCurrent == true ? administrator : session.Connection?.IsAdministrator;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ServerSettingsSource> Sources => session.Sources
        .Select(scope => new ServerSettingsSource(scope, scope.Connection.ServerKind,
            ServerAccess.CanAdminister(scope.Connection.ServerKind, scope.Connection.IsAdministrator)))
        .ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerSettingsPluginPage>> GetAdminMenuPagesAsync(CancellationToken token = default)
    {
        if (ServerKind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin)
            || InterfaceFlavor == ServerInterfaceFlavor.Generic) return [];

        try
        {
            var scope = session.Capture();
            var pages = await scope.ExecuteAsync(
                (client, ct) => client.GetServerSettingsPagesAsync(ct), token).ConfigureAwait(false);
            return scope.IsCurrent ? pages : [];
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"读取服务器插件页失败：{error.Message}");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerSettingsPluginPage>> GetAdminMenuPagesAsync(string sourceKey, CancellationToken token = default)
    {
        var source = Sources.FirstOrDefault(value => value.Key == sourceKey);
        if (source is null || source.Kind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin) || !source.IsAdministrator)
            return [];

        try
        {
            var pages = await source.Scope.ExecuteAsync(
                (client, ct) => client.GetServerSettingsPagesAsync(ct), token).ConfigureAwait(false);
            return source.IsAvailable ? pages : [];
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"读取服务器插件页失败：{error.Message}");
            return [];
        }
    }

    /// <summary>
    /// Asks the server what version it is and whether this account may administer it, and remembers both
    /// answers for the rest of the session.
    /// <para>
    /// Silent on failure. Product-specific callers decide which controls an unknown capability permits.
    /// </para>
    /// <para>
    /// Emby infers permission from the privileged system endpoint and its public fallback.
    /// Jellyfin reads the current user's Policy first: its system information response alone does not
    /// establish administrator permission.
    /// Plex has neither: it has no administrator-only server-information endpoint this client may read,
    /// so it is always reported as the restricted subset and never claims administrator. That is honest
    /// rather than limiting — Plex offers no native administration here at all (see
    /// <c>SettingsViewModel.AdministrationCategories</c>).
    /// </para>
    /// </summary>
    public async Task ProbeAsync(CancellationToken token = default)
    {
        long generation;
        lock (_gate)
        {
            generation = ++_generation;
            _scope = null;
            _serverVersion = null;
            _administrator = null;
        }

        try
        {
            var scope = session.Capture();
            var info = await scope.ExecuteAsync((client, ct) => client.GetSystemInfoAsync(ct), token)
                .ConfigureAwait(false);

            if (scope.IsCurrent && !token.IsCancellationRequested)
            {
                var version = Version.TryParse(info.Version, out var parsed) ? parsed : null;
                var administrator = !info.IsRestricted;

                lock (_gate)
                {
                    if (generation != _generation) return;
                    _scope = scope;
                    _serverVersion = version;
                    _administrator = administrator;
                }

                Log.Debug(Category, $"服务器版本 {version?.ToString() ?? "未知"}，"
                    + (administrator ? "管理员账号" : "非管理员账号"));
            }
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"读取服务器信息失败：{error.Message}");
        }
    }
}
