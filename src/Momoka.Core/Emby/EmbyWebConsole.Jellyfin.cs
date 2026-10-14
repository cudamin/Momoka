using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Momoka.Emby;

public static partial class EmbyWebConsole
{
    public const string JellyfinStorageKey = "jellyfin_credentials";

    public static string CredentialStorageKey(EmbyConnection connection) =>
        connection.IsJellyfin ? JellyfinStorageKey : StorageKey;

    public static string Root(EmbyConnection connection) => connection.IsJellyfin
        ? connection.ApiBase.GetLeftPart(UriPartial.Path).TrimEnd('/')
        : Root(connection.ApiBase);

    public static string Url(EmbyConnection connection) => connection.IsJellyfin
        ? $"{Root(connection)}/web/index.html#/dashboard"
        : Url(connection.ApiBase);

    public static string PageUrl(EmbyConnection connection, string route)
    {
        if (!connection.IsJellyfin) return PageUrl(connection.ApiBase, route);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var webRoot = new Uri($"{Root(connection)}/web/");
        if (Uri.TryCreate(route, UriKind.Absolute, out var absolute))
        {
            if (!webRoot.IsBaseOf(absolute) || absolute.UserInfo.Length > 0)
                throw new ArgumentException("配置页面不属于当前服务器。");
            return absolute.AbsoluteUri;
        }
        route = route.Trim();
        if (route.StartsWith("//", StringComparison.Ordinal) || route.Contains('\\')
            || route.Contains("..", StringComparison.Ordinal)) throw new ArgumentException("配置页面地址无效。");
        if (route.StartsWith("#!/", StringComparison.Ordinal)) route = route[3..];
        else if (route.StartsWith('#')) route = route[1..].TrimStart('/');
        else route = route.TrimStart('/');
        if (route.StartsWith("web/", StringComparison.OrdinalIgnoreCase)) route = route[4..];
        if (route.StartsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            var page = new Uri(webRoot, route);
            if (!webRoot.IsBaseOf(page)) throw new ArgumentException("配置页面不属于当前服务器。");
            return page.AbsoluteUri;
        }
        return $"{webRoot}index.html#/{route}";
    }

    /// <summary>按服务器和用户隔离 Jellyfin 浏览器状态；目录名不包含令牌或用户名。</summary>
    public static string ProfileDirectory(EmbyConnection connection, string directory) => connection.IsJellyfin
        ? Path.Combine(directory, "jellyfin-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(connection.ApiBase.AbsoluteUri + "\n" + connection.UserId)))[..32])
        : directory;

    /// <summary>
    /// Jellyfin Web 的 jellyfin_credentials 使用服务器条目上的令牌，不使用 Emby 的 Users 数组。
    /// 依据 jellyfin-web 的 ServerConnections 与 credentials 实现；令牌只写入受信 Web 根目录的本地存储。
    /// </summary>
    public static string SignInScript(EmbyConnection connection)
    {
        if (!connection.IsJellyfin)
            return SignInScript(connection.ApiBase, connection.UserId, connection.AccessToken, connection.ServerName);
        ArgumentException.ThrowIfNullOrEmpty(connection.UserId);
        ArgumentException.ThrowIfNullOrEmpty(connection.AccessToken);
        ArgumentException.ThrowIfNullOrEmpty(connection.ServerId);
        var origin = OriginLiteral(connection.ApiBase);
        var webPath = JsonSerializer.Serialize(new Uri($"{Root(connection)}/web/").AbsolutePath);
        var seed = JsonSerializer.Serialize(new
        {
            Id = connection.ServerId,
            Name = connection.ServerName,
            ManualAddress = Root(connection),
            LastConnectionMode = ManualConnectionMode,
            UserId = connection.UserId,
            AccessToken = connection.AccessToken,
            manualAddressOnly = true
        });
        var key = JsonSerializer.Serialize(JellyfinStorageKey);
        return $$"""
            (function () {
              try {
                if ((location.origin || "").toLowerCase() !== {{origin}}) return;
                if (!(location.pathname || "").startsWith({{webPath}})) return;
                var seed = {{seed}};
                var data;
                try { data = JSON.parse(localStorage.getItem({{key}}) || "null"); } catch (e) { data = null; }
                if (!data || !Array.isArray(data.Servers)) data = { Servers: [] };
                var server = data.Servers.find(function (candidate) {
                  return candidate && candidate.Id === seed.Id;
                });
                if (!server) { server = {}; data.Servers.unshift(server); }
                Object.assign(server, seed);
                server.DateLastAccessed = Date.now();
                localStorage.setItem({{key}}, JSON.stringify(data));
              } catch (e) { }
            })();
            """;
    }
}
