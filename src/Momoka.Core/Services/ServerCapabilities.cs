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
    /// The server's version, once <see cref="ProbeAsync"/> has answered, and null until then.
    /// </summary>
    Version? ServerVersion { get; }

    /// <summary>
    /// Whether the signed-in account may administer this server. Null means unknown. Jellyfin
    /// administration requires a confirmed true value; unknown permission must not expose its controls.
    /// </summary>
    bool? IsAdministrator { get; }

    Task ProbeAsync(CancellationToken token = default);
}

/// <inheritdoc cref="IServerCapabilities"/>
public sealed class ServerCapabilities(EmbySession session) : IServerCapabilities
{
    public MediaServerKind ServerKind => session.Connection?.ServerKind ?? MediaServerKind.Emby;
    public bool IsConnected => session.IsSignedIn;
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
