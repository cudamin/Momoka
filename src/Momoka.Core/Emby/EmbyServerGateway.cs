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
    public Task<PublicSystemInfo> DetectAsync(string address, CancellationToken cancellationToken) =>
        DetectAsync(address, cancellationToken, embyFamilyOnly: false);

    internal Task<PublicSystemInfo> DetectEmbyFamilyAsync(string address, CancellationToken cancellationToken) =>
        DetectAsync(address, cancellationToken, embyFamilyOnly: true);

    private async Task<PublicSystemInfo> DetectAsync(string address, CancellationToken cancellationToken, bool embyFamilyOnly)
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
        if (embyFamilyOnly) throw failure ?? new EmbyApiException("无法确认 Emby 或 Jellyfin 服务器信息");
        try
        {
            return await Momoka.FnNas.FnNasMediaAdapter.DetectAsync(http.FnNasClient, address, cancellationToken).ConfigureAwait(false);
        }
        catch (Momoka.FnNas.FnNasApiException error)
        {
            // 路由不存在仍可继续探测 Plex；已返回的身份／签名／服务故障必须保留，不能被最后一个 404 覆盖。
            if (error.StatusCode != System.Net.HttpStatusCode.NotFound && error.Code is not (-6 or -13 or -60))
                failure = Momoka.FnNas.FnNasMediaAdapter.Translate(error);
        }
        try
        {
            return await Momoka.Plex.PlexMediaAdapter
                .DetectAsync(http.PlexClient, address, PlexCall, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Momoka.Plex.PlexApiException)
        {
            throw failure ?? new EmbyApiException("无法识别媒体服务器");
        }
    }

    /// <summary>Plex's anonymous probe identifies the client but carries no credential.</summary>
    private Momoka.Plex.PlexCall PlexCall => Momoka.Plex.PlexCall.For(null, device.DeviceId, device.Version);

    /// <summary>
    /// The users the server is willing to advertise, so the login page can offer a
    /// picker instead of making the user type a username.
    /// <para>
    /// Empty for 飞牛影视 and Plex, which do not advertise a user list at all: a Plex server has no such
    /// endpoint, and an account there belongs to Plex rather than to the server. The login page is built
    /// to carry on without the list (see <c>SignInViewModel.ListUsersAsync</c>).
    /// </para>
    /// </summary>
    public Task<List<EmbyUser>> GetPublicUsersAsync(Uri apiBase, CancellationToken cancellationToken,
        MediaServerKind kind = MediaServerKind.Emby) =>
        kind is MediaServerKind.FnNas or MediaServerKind.Plex ? Task.FromResult(new List<EmbyUser>())
        : http.GetJsonAsync<List<EmbyUser>>(EmbyUrl.Combine(apiBase, "Users/Public"), new(device, null, ServerKind: kind), cancellationToken);

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

        if (kind == MediaServerKind.FnNas)
            return await Momoka.FnNas.FnNasMediaAdapter.SignInAsync(http.FnNasClient, apiBase, username, password, device,
                serverVersion, serverId, cancellationToken).ConfigureAwait(false);

        if (kind == MediaServerKind.Plex)
            return await Momoka.Plex.PlexMediaAdapter.SignInAsync(http.PlexClient, apiBase, username, password, device,
                serverVersion, serverId, cancellationToken).ConfigureAwait(false);

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

            // 公开信息纠正产品；直接登录没有原始入口探测证据时，保留刚刚认证成功的完整路径。
            // 保存令牌恢复由 EmbySession 根据原始入口匿名探测、确认服务器和用户后再采用地址。
            if (info.ServerKind != kind)
            {
                Log.Info(Category, $"服务器产品识别为 {info.ServerKind}，纠正登录时假定的 {kind}");
                var assumed = kind;
                kind = info.ServerKind;
                apiBase = EmbyServerAddress.Rebase(apiBase, assumed, kind);
            }
        }
        catch (EmbyApiException error)
        {
            // 此处读取失败只回退名称；读取成功会更新产品与版本，不能把整个读取当成纯装饰。
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
