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
    /// <summary>
    /// The server's version, once <see cref="ProbeAsync"/> has answered, and null until then.
    /// </summary>
    Version? ServerVersion { get; }

    /// <summary>
    /// Whether the signed-in account may administer this server. Null means "not known yet" — nobody is
    /// signed in, the probe has not come back, or it failed — and every caller is expected to read that as
    /// 「offer it anyway」: hiding something the user is entitled to is the worse of the two mistakes.
    /// </summary>
    bool? IsAdministrator { get; }

    Task ProbeAsync(CancellationToken token = default);
}

/// <inheritdoc cref="IServerCapabilities"/>
public sealed class ServerCapabilities(EmbySession session) : IServerCapabilities
{
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
            return scope?.IsCurrent == true ? version : null;
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
            return scope?.IsCurrent == true ? administrator : null;
        }
    }

    /// <summary>
    /// Asks the server what version it is and whether this account may administer it, and remembers both
    /// answers for the rest of the session.
    /// <para>
    /// Fire and forget, and silent on failure. Nothing blocks on the answer: every caller treats a null
    /// version as "offer the key anyway" and a null administrator as 「show it anyway」, so a server that
    /// will not say is no worse off than one that has not been asked yet.
    /// </para>
    /// <para>
    /// One round trip, not two. <c>/System/Info</c> is administrators-only, and
    /// <see cref="EmbyClient.GetSystemInfoAsync"/> already falls back to the public subset for everyone
    /// else — which is the same question asked and answered, so the account's standing comes out of it for
    /// free rather than out of a second call to <c>/Users/{id}</c>.
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
