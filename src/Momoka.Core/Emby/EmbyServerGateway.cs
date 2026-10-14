using System.Text.Json.Serialization;
using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>
/// Operations that work before (or without) a sign-in: probing a server and
/// authenticating. Split from <see cref="EmbyClient"/> so an authenticated client is
/// only ever constructed from a real session.
/// </summary>
public sealed class EmbyServerGateway(EmbyHttp http, DeviceIdentity device)
{
    private const string Category = "emby";

    private EmbyHttp.RequestContext Anonymous => new(device, null);

    /// <summary>Used by the "测试连接" button; also confirms the address really is an Emby server.</summary>
    public Task<PublicSystemInfo> GetPublicSystemInfoAsync(Uri apiBase, CancellationToken cancellationToken) =>
        http.GetJsonAsync<PublicSystemInfo>(EmbyUrl.Combine(apiBase, "System/Info/Public"), Anonymous, cancellationToken);

    /// <summary>Identify the product anonymously before sending a password or a saved token.</summary>
    public async Task<PublicSystemInfo> DetectAsync(string address, CancellationToken cancellationToken)
    {
        var plain = EmbyServerAddress.Normalize(address, MediaServerKind.Jellyfin);
        var emby = EmbyServerAddress.Normalize(address);
        var candidates = new[] { plain, emby }.Distinct();
        EmbyApiException? failure = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var info = await GetPublicSystemInfoAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(info.Id) && string.IsNullOrWhiteSpace(info.ServerName)
                    && string.IsNullOrWhiteSpace(info.Version))
                    throw new EmbyApiException("该地址未返回可识别的媒体服务器信息");
                info.ApiBase = info.ServerKind == MediaServerKind.Jellyfin ? candidate : emby;
                return info;
            }
            catch (EmbyApiException error) when (error is not EmbyUnreachableException
                && error.StatusCode is null or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.OK)
            {
                failure = error;
            }
        }
        throw failure ?? new EmbyApiException("无法识别媒体服务器");
    }

    /// <summary>
    /// The users the server is willing to advertise, so the login page can offer a
    /// picker instead of making the user type a username.
    /// </summary>
    public Task<List<EmbyUser>> GetPublicUsersAsync(Uri apiBase, CancellationToken cancellationToken,
        MediaServerKind kind = MediaServerKind.Emby) =>
        http.GetJsonAsync<List<EmbyUser>>(EmbyUrl.Combine(apiBase, "Users/Public"), new(device, null, ServerKind: kind), cancellationToken);

    public async Task<EmbyConnection> AuthenticateAsync(
        Uri apiBase,
        string username,
        string password,
        CancellationToken cancellationToken,
        MediaServerKind kind = MediaServerKind.Emby,
        string? serverVersion = null,
        string? serverId = null)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new EmbyAuthenticationException("请填写用户名");

        var url = EmbyUrl.Combine(apiBase, "Users/AuthenticateByName");
        object body = kind == MediaServerKind.Jellyfin
            ? new { Username = username.Trim(), Pw = password }
            : new AuthenticateByNameRequest { Username = username.Trim(), Pw = password };
        var context = new EmbyHttp.RequestContext(device, null, IsAuthenticationAttempt: true, ServerKind: kind);

        var result = await http.PostJsonAsync<AuthenticationResult>(url, body, context, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.User?.Id))
            throw new EmbyApiException("服务器返回的登录结果缺少访问令牌或用户 ID");

        string serverName;
        try
        {
            var info = await GetPublicSystemInfoAsync(apiBase, cancellationToken).ConfigureAwait(false);
            serverName = info.ServerName ?? apiBase.Host;
            serverVersion = info.Version ?? serverVersion;
            serverId = info.Id ?? serverId;
        }
        catch (EmbyApiException error)
        {
            // Cosmetic only: a nameless server should not fail an otherwise good login.
            Log.Warn(Category, "登录成功但无法读取服务器名称", error);
            serverName = apiBase.Host;
        }

        Log.Info(Category, $"已登录 {serverName}（用户 {result.User!.Name}）");
        return new EmbyConnection(apiBase, result.AccessToken, result.User.Id, result.User.Name, serverName, device)
        {
            ServerKind = kind,
            ServerVersion = serverVersion,
            ServerId = result.ServerId ?? serverId,
            IsAdministrator = kind == MediaServerKind.Jellyfin ? result.User.Policy?.IsAdministrator : null
        };
    }

    private sealed class AuthenticateByNameRequest
    {
        /// <summary>
        /// Emby 4.9.5+ binds the body case-sensitively and reads <c>UserName</c>; older servers
        /// deserialize case-insensitively, so this one spelling works on every version.
        /// </summary>
        [JsonPropertyName("UserName")]
        public string Username { get; set; } = "";

        /// <summary>Emby's field name for the plain password.</summary>
        public string Pw { get; set; } = "";
    }
}
