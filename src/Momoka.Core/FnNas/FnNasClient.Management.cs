using System.Text.Json.Nodes;

namespace Momoka.FnNas;

public sealed partial class FnNasClient
{
    public Task<IReadOnlyList<JsonObject>> GetAdminLibrariesAsync(FnNasConnection c, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/mdb/list", null, ct);

    public Task<JsonObject> GetLibraryAsync(FnNasConnection c, string guid, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/mdb/" + FnNasEndpoint.Segment(guid), null, ct);

    public Task CreateLibraryAsync(FnNasConnection c, JsonObject draft, CancellationToken ct = default)
    {
        FnNasLibraryInput.Validate(draft, editing: false);
        return AdminActionAsync(c, HttpMethod.Put, "api/v1/mdb/create", WithoutGuid(draft), ct);
    }

    public Task UpdateLibraryAsync(FnNasConnection c, string guid, JsonObject draft, CancellationToken ct = default)
    {
        FnNasLibraryInput.Validate(draft, editing: true);
        return AdminActionAsync(c, HttpMethod.Post, "api/v1/mdb/" + FnNasEndpoint.Segment(guid), WithoutGuid(draft), ct);
    }

    public Task SetLibraryOrderAsync(FnNasConnection c, IReadOnlyList<string> guids, CancellationToken ct = default)
    {
        var values = new JsonArray();
        for (var index = 0; index < guids.Count; index++)
        {
            _ = FnNasEndpoint.Segment(guids[index]);
            values.Add(new JsonObject { ["guid"] = guids[index], ["sort_num"] = index });
        }
        return AdminActionAsync(c, HttpMethod.Post, "api/v1/mdb/setSort", new JsonObject { ["list_sort"] = values }, ct);
    }

    public async Task<string> UploadIptvSourceAsync(FnNasConnection c, byte[] bytes, string filename, CancellationToken ct = default)
    {
        FnNasLibraryInput.ValidateUpload(bytes, filename);
        await RequireAdminAsync(c, ct).ConfigureAwait(false);
        // The frontend's multipart upload uses Authorization only; it is not signed as JSON.
        c.ThrowIfNotCurrent();
        var data = await http.UploadAsync(FnNasEndpoint.Api(c.ApiBase, "api/v1/mdb/iptv/temp/upload"), bytes, filename, c.AccessToken, ct).ConfigureAwait(false);
        if (data?["temp_file_id"] is not JsonValue id || !id.TryGetValue<string>(out var value) || string.IsNullOrEmpty(value))
            throw new FnNasApiException("飞牛影视上传响应缺少临时文件标识");
        return value;
    }

    public Task DeleteLibraryAsync(FnNasConnection c, string guid, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Delete, "api/v1/mdb/" + FnNasEndpoint.Segment(guid), null, ct);

    public Task ScanLibraryAsync(FnNasConnection c, string guid, IReadOnlyList<string>? directories = null, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/mdb/scan/" + FnNasEndpoint.Segment(guid),
            directories is { Count: > 0 } ? new JsonObject { ["dir_list"] = Strings(directories) } : null, ct);

    public Task ScanAllLibrariesAsync(FnNasConnection c, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/mdb/scanall", null, ct);

    public Task<JsonObject> GetAuthorizedDirectoriesAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/server/getAppAuthorizedDir", null, ct);

    public Task<IReadOnlyList<JsonObject>> BrowseDirectoriesAsync(FnNasConnection c, string? path = null, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Post, "api/v1/server/path", string.IsNullOrEmpty(path) ? new JsonObject() : new JsonObject { ["path"] = path }, ct);

    public Task<IReadOnlyList<JsonObject>> GetUsersAsync(FnNasConnection c, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/manager/user/list", null, ct);

    public Task CreateUserAsync(FnNasConnection c, JsonObject draft, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Put, "api/v2/manager/user/create", UserBody(draft, creating: true), ct);

    public Task UpdateUserAsync(FnNasConnection c, string guid, JsonObject draft, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v2/manager/user/" + FnNasEndpoint.Segment(guid), UserBody(draft, creating: false), ct);

    public Task DeleteUserAsync(FnNasConnection c, string guid, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Delete, "api/v1/manager/user/" + FnNasEndpoint.Segment(guid), null, ct);

    public Task UnlockUserAsync(FnNasConnection c, string guid, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/manager/user/unlock", new JsonObject { ["guid"] = guid }, ct);

    public Task<JsonObject> GetDefaultPermissionAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/manager/template/permission", null, ct);

    public Task UpdateDefaultPermissionAsync(FnNasConnection c, JsonObject draft, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/manager/template/permission", draft.DeepClone(), ct);

    public Task<JsonObject> GetServerSettingsAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/server/info", null, ct);

    public async Task UpdateServerSettingsAsync(FnNasConnection c, JsonObject draft, CancellationToken ct = default)
    {
        await RequireAdminAsync(c, ct).ConfigureAwait(false);
        // The official form sends the complete document. Merge only the settings exposed by Momoka,
        // so excluded settings and future server fields retain their current values.
        var current = await GetServerSettingsAsync(c, ct).ConfigureAwait(false);
        foreach (var field in new[] { "name", "lan", "file_monitor", "meta_dir_vol_index", "mediasrv_cache_vol_index" })
            if (draft.TryGetPropertyValue(field, out var value)) current[field] = value?.DeepClone();
        await ActionAsync(c, HttpMethod.Post, "api/v1/server/info", current, ct).ConfigureAwait(false);
    }

    public Task<JsonObject> GetStorageVolumesAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/server/allVols", null, ct);

    public Task<JsonObject> GetSearchIndexStatusAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Get, "api/v1/search/indexStatus", null, ct);

    public Task RebuildSearchIndexAsync(FnNasConnection c, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/search/rebuild", null, ct);

    public Task<IReadOnlyList<JsonObject>> GetScheduledTasksAsync(FnNasConnection c, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/task/schedule/list", null, ct);

    public Task<JsonObject> GetScheduleSettingsAsync(FnNasConnection c, CancellationToken ct = default) =>
        ObjectAsync(c, HttpMethod.Post, "api/v1/task/schedule/getSetting", null, ct);

    public Task SetScheduledTasksEnabledAsync(FnNasConnection c, bool enabled, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/task/schedule/set", new JsonObject { ["task_switch"] = enabled ? "1" : "2" }, ct);

    public Task<IReadOnlyList<JsonObject>> GetRunningTasksAsync(FnNasConnection c, CancellationToken ct = default) =>
        ListAsync(c, HttpMethod.Get, "api/v1/task/running", null, ct);

    public Task StopTaskAsync(FnNasConnection c, string guid, string taskType, CancellationToken ct = default) =>
        AdminActionAsync(c, HttpMethod.Post, "api/v1/task/stop", new JsonObject { ["guid"] = guid, ["type"] = taskType }, ct);

    private async Task AdminActionAsync(FnNasConnection c, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        await RequireAdminAsync(c, ct).ConfigureAwait(false);
        await ActionAsync(c, method, path, body, ct).ConfigureAwait(false);
    }

    private async Task RequireAdminAsync(FnNasConnection c, CancellationToken ct)
    {
        var user = await GetUserAsync(c, ct).ConfigureAwait(false);
        if (!FnNasMediaAdapter.Flag(user, "is_admin"))
            throw FnNasApiException.Business(-5);
    }

    private static JsonObject WithoutGuid(JsonObject draft)
    {
        var body = (JsonObject)draft.DeepClone();
        body.Remove("guid");
        return body;
    }

    private static JsonObject UserBody(JsonObject draft, bool creating)
    {
        var body = WithoutGuid(draft);
        if (body["password"] is JsonValue password && password.TryGetValue<string>(out var plaintext) && plaintext.Length > 0)
            body["password"] = FnNasSigner.HashPassword(plaintext);
        // The official v2 edit form sends an empty password to retain the current password.
        if (creating) body["is_admin"] = 0;
        return body;
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
