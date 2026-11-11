using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnMusic;

public sealed record FnMusicConnection(Uri Mount, string Token);

/// <summary>Music endpoint spellings and JSON casing from the official 1.1.19 SDK registry.</summary>
public sealed class FnMusicClient(FnMusicHttp http)
{
    public async Task<JsonObject> GetConfigurationAsync(Uri mount, CancellationToken ct) =>
        Object(await http.SendAsync(HttpMethod.Get, FnMusicEndpoint.Api(mount, "sys/config"), null, null, ct).ConfigureAwait(false));

    public async Task<JsonObject> LoginAsync(Uri mount, string username, string password, string deviceId, CancellationToken ct) =>
        Object(await http.SendAsync(HttpMethod.Post, FnMusicEndpoint.Api(mount, "user/password-login"), new JsonObject
        { ["username"] = username.Trim(), ["password"] = FnMusicSigner.HashPassword(password), ["deviceId"] = FnMusicSigner.DeviceId(deviceId) }, null, ct).ConfigureAwait(false));

    public async Task<JsonObject> GetAsync(FnMusicConnection connection, string path, CancellationToken ct, params (string Key, string? Value)[] query) =>
        Object(await http.SendAsync(HttpMethod.Get, FnMusicEndpoint.Api(connection.Mount, path, query), null, connection.Token, ct).ConfigureAwait(false));

    public async Task PostAsync(FnMusicConnection connection, string path, JsonObject? body, CancellationToken ct) =>
        _ = await http.SendAsync(HttpMethod.Post, FnMusicEndpoint.Api(connection.Mount, path), body, connection.Token, ct).ConfigureAwait(false);

    public Task<byte[]> GetCoverAsync(FnMusicConnection connection, string coverId, int size, CancellationToken ct) =>
        http.GetImageAsync(FnMusicEndpoint.Api(connection.Mount, "static/cover", ("coverId", coverId),
            ("size", size.ToString(System.Globalization.CultureInfo.InvariantCulture))), connection.Token, ct);

    internal static JsonObject Object(JsonNode? value) => value as JsonObject ?? throw new EmbyApiException("飞牛音乐响应缺少有效对象");
}
