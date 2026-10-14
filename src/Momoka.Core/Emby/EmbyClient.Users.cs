using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public async Task<IReadOnlyList<EmbyManagedUser>> GetManagedUsersAsync(CancellationToken token)
    {
        if (Connection.IsJellyfin)
        {
            var document = await http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Users"), AdministrationContext, token).ConfigureAwait(false);
            var result = document.OfType<JsonObject>().Select(item => new EmbyManagedUser(item)).ToArray();
            if (result.Length != document.Count || result.Any(user => user.Id.Length == 0)
                || result.Select(user => user.Id).Distinct(StringComparer.Ordinal).Count() != result.Length)
                throw new InvalidDataException("服务器返回了重复或无效的用户列表。");
            return result;
        }
        var users = new List<EmbyManagedUser>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; ;)
        {
            var result = await http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Users/Query",
                ("StartIndex", start.ToString(CultureInfo.InvariantCulture)), ("Limit", "200"),
                ("SortBy", "SortName"), ("SortOrder", "Ascending")), AdministrationContext, token).ConfigureAwait(false);
            var page = (result["Items"] as JsonArray ?? throw new InvalidDataException("服务器没有返回用户列表。"))
                .OfType<JsonObject>().Select(item => new EmbyManagedUser(item)).ToArray();
            foreach (var user in page)
                if (user.Id.Length == 0 || !ids.Add(user.Id)) throw new InvalidDataException("服务器返回了重复或无效的用户分页。");
            users.AddRange(page);
            start += page.Length;
            var total = int.TryParse(result["TotalRecordCount"]?.ToString(), out var count) ? count : start;
            if (start >= total) return users;
            if (page.Length == 0) throw new InvalidDataException("用户列表分页尚未读取完整，请刷新重试。");
        }
    }

    public async Task<EmbyManagedUser> GetManagedUserAsync(string id, CancellationToken token) =>
        new(await http.GetJsonAsync<JsonObject>(UserUrl(id), Context, token).ConfigureAwait(false));

    public async Task<EmbyManagedUser> CreateManagedUserAsync(string name, string? copyFrom, IEnumerable<string> copyOptions, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var body = new JsonObject { ["Name"] = name.Trim() };
        if (Connection.IsJellyfin && !string.IsNullOrEmpty(copyFrom)) throw new NotSupportedException("Jellyfin 不支持复制用户数据。");
        if (!string.IsNullOrEmpty(copyFrom))
        {
            body["CopyFromUserId"] = copyFrom;
            body["UserCopyOptions"] = EmbyUserPermissions.Array(copyOptions);
        }
        return new(await http.PostJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Users/New"), body, AdministrationContext, token).ConfigureAwait(false));
    }

    public Task UpdateManagedUserAsync(string id, JsonObject user, CancellationToken token) =>
        Connection.UsesJellyfin12Routes ? throw new NotSupportedException("此 Jellyfin 版本不支持更改用户名。") :
        http.PostAsync(UserUrl(id), user, AdministrationContext, token);

    public Task UpdateManagedUserPolicyAsync(string id, JsonObject policy, CancellationToken token) =>
        http.PostAsync(UserUrl(id, "Policy"), policy, AdministrationContext, token);

    public Task DeleteManagedUserAsync(string id, CancellationToken token) =>
        Connection.IsJellyfin ? http.DeleteAsync(UserUrl(id), AdministrationContext, token) :
        http.PostAsync(UserUrl(id, "Delete"), null, AdministrationContext, token);

    public Task UpdateManagedUserPasswordAsync(string id, string password, CancellationToken token, string currentPassword = "") =>
        http.PostAsync(Connection.UsesJellyfin12Routes
            ? EmbyUrl.Combine(ApiBase, "Users/Password", ("userId", UserQueryId(id))) : UserUrl(id, "Password"),
            new { CurrentPw = Connection.IsJellyfin ? currentPassword : "", NewPw = password }, AdministrationContext, token);

    public Task UpdateManagedUserPinAsync(string id, string pin, CancellationToken token)
    {
        if (Connection.IsJellyfin) throw new NotSupportedException("Jellyfin 不支持个人 PIN。");
        EmbyUserPermissions.ValidatePin(pin);
        return http.PostAsync(UserUrl(id, "Configuration/Partial"),
            new JsonObject { ["ProfilePin"] = pin.Length == 0 ? null : pin }, AdministrationContext, token);
    }

    public Task<JsonObject> LinkManagedUserAsync(string id, string connectName, CancellationToken token) =>
        Connection.IsJellyfin ? throw new NotSupportedException("Jellyfin 不支持 Emby Connect。") :
        http.PostJsonAsync<JsonObject>(UserUrl(id, "Connect/Link"), new { ConnectUsername = connectName }, AdministrationContext, token);

    public Task UnlinkManagedUserAsync(string id, CancellationToken token) =>
        Connection.IsJellyfin ? throw new NotSupportedException("Jellyfin 不支持 Emby Connect。") :
        http.PostAsync(UserUrl(id, "Connect/Link/Delete"), null, AdministrationContext, token);

    public Task<byte[]> GetManagedUserImageAsync(string id, string tag, CancellationToken token) =>
        http.GetBytesAsync(ManagedUserImageUrl(id, tag), Context, token);

    // 官方用户图片上传接口发送 Base64 文本，不同于条目图片上传的原始字节。
    public Task UploadManagedUserImageAsync(string id, byte[] bytes, string contentType, CancellationToken token)
    {
        if (contentType is not ("image/jpeg" or "image/png")) throw new ArgumentException("用户头像支持 JPG 和 PNG 图片。");
        return http.PostBytesAsync(ManagedUserImageUrl(id), Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes)), contentType, AdministrationContext, token);
    }

    public Task DeleteManagedUserImageAsync(string id, CancellationToken token) =>
        Connection.IsJellyfin ? http.DeleteAsync(ManagedUserImageUrl(id), AdministrationContext, token) :
        http.PostAsync(UserUrl(id, "Images/Primary/Delete"), null, AdministrationContext, token);

    public async Task<JsonArray> GetUserSelectableFoldersAsync(CancellationToken token)
    {
        if (!Connection.IsJellyfin)
            return await http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Library/SelectableMediaFolders"), AdministrationContext, token).ConfigureAwait(false);
        var folders = await GetVirtualFoldersAsync(token).ConfigureAwait(false);
        return new JsonArray(folders.Select(folder => (JsonNode)new JsonObject { ["Id"] = folder.Id, ["Name"] = folder.Name }).ToArray());
    }

    public Task<JsonArray> GetUserAuthenticationProvidersAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Auth/Providers"), AdministrationContext, token);

    public Task<JsonArray> GetUserFeaturesAsync(CancellationToken token) =>
        Connection.IsJellyfin ? throw new NotSupportedException("Jellyfin 不提供 Emby 功能列表。") :
        http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Features", ("FeatureType", "User")), AdministrationContext, token);

    public Task<JsonArray> GetUserRatingsAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Localization/ParentalRatings"), AdministrationContext, token);

    public Task<JsonObject> GetUserChannelsAsync(bool forDeletion, CancellationToken token) =>
        http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Channels",
            ("SupportsMediaDeletion", forDeletion ? "true" : null)), AdministrationContext, token);

    public Task<JsonObject> GetUserDevicesAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Devices"), AdministrationContext, token);

    public Task<JsonObject> GetUserCopyOptionsAsync(CancellationToken token) =>
        Connection.IsJellyfin ? throw new NotSupportedException("Jellyfin 不支持复制用户数据。") :
        http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Users/CopyDataOptions"), AdministrationContext, token);

    public async Task<UserEditorOptions> GetUserEditorOptionsAsync(CancellationToken token)
    {
        var folders = GetUserSelectableFoldersAsync(token);
        var providers = GetUserAuthenticationProvidersAsync(token);
        var features = Connection.IsJellyfin ? Task.FromResult(new JsonArray()) : GetUserFeaturesAsync(token);
        var ratings = GetUserRatingsAsync(token);
        var channels = GetUserChannelsAsync(false, token);
        var deleteChannels = GetUserChannelsAsync(true, token);
        var devices = GetUserDevicesAsync(token);
        var copy = Connection.IsJellyfin ? Task.FromResult(new JsonObject()) : GetUserCopyOptionsAsync(token);
        var configuration = GetServerConfigurationAsync(token);
        var deleteFolders = http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Library/MediaFolders", ("IsHidden", "false")), AdministrationContext, token);
        await Task.WhenAll(folders, providers, features, ratings, channels, deleteChannels, devices, copy, configuration, deleteFolders).ConfigureAwait(false);
        return new(await folders.ConfigureAwait(false), await providers.ConfigureAwait(false), await features.ConfigureAwait(false),
            await ratings.ConfigureAwait(false), await channels.ConfigureAwait(false), await deleteChannels.ConfigureAwait(false),
            await devices.ConfigureAwait(false), await copy.ConfigureAwait(false), await configuration.ConfigureAwait(false), await deleteFolders.ConfigureAwait(false));
    }

    public Task CopyManagedUserDataAsync(string id, string sourceId, IEnumerable<string> options, CancellationToken token) =>
        Connection.IsJellyfin ? throw new NotSupportedException("Jellyfin 不支持复制用户数据。") :
        http.PostAsync(UserUrl(sourceId, "CopyData"), new { ToUserIds = new[] { id }, CopyOptions = options.ToArray() }, AdministrationContext, token);

    private Uri ManagedUserImageUrl(string id, string? tag = null) => Connection.UsesJellyfin12Routes
        ? EmbyUrl.Combine(ApiBase, "UserImage", ("userId", UserQueryId(id)), ("tag", tag))
        : EmbyUrl.Combine(ApiBase, $"Users/{UserSegment(id)}/Images/Primary", ("tag", tag), ("maxWidth", tag is null ? null : "240"));

    private Uri UserUrl(string id, string suffix = "") => EmbyUrl.Combine(ApiBase,
        $"Users/{UserSegment(id)}" + (suffix.Length == 0 ? "" : "/" + suffix));

    private static string UserQueryId(string id)
    {
        _ = UserSegment(id);
        return id;
    }

    private static string UserSegment(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or ".." || id.IndexOfAny(['/', '\\', '?', '#']) >= 0) throw new ArgumentException("用户 ID 无效。");
        return Uri.EscapeDataString(id);
    }
}

public sealed record UserEditorOptions(JsonArray Folders, JsonArray Providers, JsonArray Features, JsonArray Ratings,
    JsonObject Channels, JsonObject DeleteChannels, JsonObject Devices, JsonObject CopyOptions, JsonObject ServerConfiguration, JsonObject DeleteFolders);
