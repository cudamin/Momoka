using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>密码只属于输入时的服务器和账号，不跟随地址框或用户列表迁移。</summary>
public static class EmbySignInIdentity
{
    public static bool SameAddress(string? left, string? right, MediaServerKind kind = MediaServerKind.Emby) =>
        EmbyServerAddress.TryNormalize(left ?? "", out var first, out _, kind)
            && EmbyServerAddress.TryNormalize(right ?? "", out var second, out _, kind)
            && first == second;

    public static bool SameUsername(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static AccountProfile? SavedAccount(IEnumerable<ServerProfile> servers, string address, string username) =>
        servers.FirstOrDefault(server => SameAddress(server.Url, address, server.ServerKind))?.Accounts
            .FirstOrDefault(account => SameUsername(account.Username, username));

    public static bool IsPasswordless(string address, Uri? usersAddress, string username, EmbyUser? selected,
        MediaServerKind kind = MediaServerKind.Emby) =>
        usersAddress is not null && SameAddress(address, usersAddress.AbsoluteUri, kind)
            && selected is { HasPassword: false } && SameUsername(selected.Name, username);

    public static string PasswordFor(
        string address,
        string username,
        string password,
        string passwordAddress,
        string passwordUsername,
        bool passwordless,
        MediaServerKind kind = MediaServerKind.Emby) =>
        !passwordless && SameAddress(address, passwordAddress, kind) && SameUsername(username, passwordUsername)
            ? password : "";
}
