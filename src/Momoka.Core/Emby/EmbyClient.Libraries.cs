using System.Globalization;
using System.Text.Json.Nodes;

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public async Task<IReadOnlyList<EmbyVirtualFolder>> GetVirtualFoldersAsync(CancellationToken token)
    {
        if (Connection.IsJellyfin)
        {
            var document = await http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders"), AdministrationContext, token).ConfigureAwait(false);
            var result = document.OfType<JsonObject>().Select(item => new EmbyVirtualFolder(item)).ToArray();
            if (result.Length != document.Count || result.Any(folder => folder.Id.Length == 0)
                || result.Select(folder => folder.Id).Distinct(StringComparer.Ordinal).Count() != result.Length)
                throw new InvalidDataException("服务器返回了重复或无效的媒体库列表。");
            return result;
        }
        var folders = new List<EmbyVirtualFolder>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; ;)
        {
            var result = await http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Query",
                ("StartIndex", start.ToString(CultureInfo.InvariantCulture)), ("Limit", "200")), AdministrationContext, token).ConfigureAwait(false);
            var items = result["Items"] as JsonArray ?? throw new InvalidDataException("服务器没有返回媒体库列表。");
            if (items.Any(item => item is not JsonObject)) throw new InvalidDataException("媒体库分页包含无效条目。");
            foreach (var item in items.OfType<JsonObject>())
            {
                var folder = new EmbyVirtualFolder(item);
                if (folder.Id.Length == 0 || !ids.Add(folder.Id)) throw new InvalidDataException("媒体库分页包含重复或无效的条目。");
                folders.Add(folder);
            }
            start += items.Count;
            var total = int.TryParse(result["TotalRecordCount"]?.ToString(), out var count) ? count : start;
            if (start >= total) return folders;
            if (items.Count == 0) throw new InvalidDataException("媒体库列表尚未读取完整，请重试。");
        }
    }

    public Task<JsonObject> GetLibraryAvailableOptionsAsync(string contentType, bool isNew, CancellationToken token) =>
        http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Libraries/AvailableOptions",
            ("LibraryContentType", Connection.IsJellyfin && string.IsNullOrEmpty(contentType) ? null : contentType), ("IsNewLibrary", isNew ? "true" : "false")), AdministrationContext, token);

    public Task<JsonArray> GetLibraryCulturesAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Localization/Cultures"), AdministrationContext, token);

    public Task<JsonArray> GetLibraryCountriesAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Localization/Countries"), AdministrationContext, token);

    public Task CreateVirtualFolderAsync(string name, string contentType, JsonObject options, CancellationToken token)
    {
        EmbyLibraryOptions.ValidateNew(name, options);
        return http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders",
            ("name", name.Trim()), ("collectionType", string.IsNullOrEmpty(contentType) ? null : contentType),
            ("refreshLibrary", "true")), new JsonObject { ["LibraryOptions"] = options.DeepClone() }, AdministrationContext, token);
    }

    public Task SaveVirtualFolderOptionsAsync(string id, JsonObject options, CancellationToken token) =>
        http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/LibraryOptions"),
            new JsonObject { ["Id"] = LibraryId(id), ["LibraryOptions"] = options.DeepClone() }, AdministrationContext, token);

    public async Task RenameVirtualFolderAsync(string id, string name, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Connection.IsJellyfin)
        {
            var current = await GetVirtualFolderNameAsync(id, token).ConfigureAwait(false);
            await http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Name", ("name", current),
                ("newName", name.Trim()), ("refreshLibrary", "true")), null, AdministrationContext, token).ConfigureAwait(false);
            return;
        }
        await http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Name"),
            new { Id = LibraryId(id), NewName = name.Trim() }, AdministrationContext, token).ConfigureAwait(false);
    }

    public async Task RemoveVirtualFolderAsync(string id, CancellationToken token)
    {
        if (Connection.IsJellyfin)
        {
            var name = await GetVirtualFolderNameAsync(id, token).ConfigureAwait(false);
            await http.DeleteAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders", ("name", name),
                ("refreshLibrary", "true")), AdministrationContext, token).ConfigureAwait(false);
            return;
        }
        await http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Delete"), new { Id = LibraryId(id) }, AdministrationContext, token).ConfigureAwait(false);
    }

    public Task AddLibraryPathAsync(string id, JsonObject pathInfo, CancellationToken token) =>
        SaveLibraryPathAsync(id, pathInfo, false, token);

    public Task UpdateLibraryPathAsync(string id, JsonObject pathInfo, CancellationToken token) =>
        SaveLibraryPathAsync(id, pathInfo, true, token);

    private async Task SaveLibraryPathAsync(string id, JsonObject pathInfo, bool update, CancellationToken token)
    {
        var body = new JsonObject { ["PathInfo"] = pathInfo.DeepClone() };
        body[Connection.IsJellyfin ? "Name" : "Id"] = Connection.IsJellyfin
            ? await GetVirtualFolderNameAsync(id, token).ConfigureAwait(false) : LibraryId(id);
        await http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Paths" + (update ? "/Update" : ""),
            ("refreshLibrary", update ? null : "true")), body, AdministrationContext, token).ConfigureAwait(false);
    }

    public async Task RemoveLibraryPathAsync(string id, string path, CancellationToken token)
    {
        if (Connection.IsJellyfin)
        {
            var name = await GetVirtualFolderNameAsync(id, token).ConfigureAwait(false);
            await http.DeleteAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Paths", ("name", name),
                ("path", path), ("refreshLibrary", "true")), AdministrationContext, token).ConfigureAwait(false);
            return;
        }
        await http.PostAsync(EmbyUrl.Combine(ApiBase, "Library/VirtualFolders/Paths/Delete"),
            new { Id = LibraryId(id), Path = path, RefreshLibrary = true }, AdministrationContext, token).ConfigureAwait(false);
    }

    private async Task<string> GetVirtualFolderNameAsync(string id, CancellationToken token)
    {
        _ = LibraryId(id);
        var folders = await GetVirtualFoldersAsync(token).ConfigureAwait(false);
        var folder = folders.FirstOrDefault(folder => folder.Id == id)
            ?? throw new InvalidOperationException("此媒体库已被移除，请刷新列表。");
        if (string.IsNullOrWhiteSpace(folder.Name)) throw new InvalidDataException("服务器未返回媒体库名称。");
        return folder.Name;
    }

    public Task<JsonArray> GetLibraryTasksAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "ScheduledTasks"), AdministrationContext, token);

    public Task SetLibraryTaskRunningAsync(string id, bool running, CancellationToken token) =>
        Connection.IsJellyfin && !running
            ? http.DeleteAsync(EmbyUrl.Combine(ApiBase, $"ScheduledTasks/Running/{LibraryId(id)}"), AdministrationContext, token)
            : http.PostAsync(EmbyUrl.Combine(ApiBase, $"ScheduledTasks/Running/{LibraryId(id)}" + (running ? "" : "/Delete")), null, AdministrationContext, token);

    public Task ScanVirtualFolderAsync(string id, string mode, bool replaceImages, CancellationToken token, bool replaceThumbnails = false)
    {
        if (Connection.IsJellyfin && replaceThumbnails) throw new NotSupportedException("Jellyfin 不支持替换 Emby 视频预览缩略图。");
        if (mode is not ("scan" or "missing" or "all")) throw new ArgumentException("未知的媒体库扫描模式。");
        return http.PostAsync(EmbyUrl.Combine(ApiBase, $"Items/{LibraryId(id)}/Refresh",
            ("Recursive", Connection.IsJellyfin ? null : "true"), ("MetadataRefreshMode", mode == "scan" ? "Default" : "FullRefresh"),
            ("ImageRefreshMode", mode == "scan" ? "Default" : "FullRefresh"),
            ("ReplaceAllMetadata", mode == "all" ? "true" : "false"),
            ("ReplaceAllImages", mode != "scan" && replaceImages ? "true" : "false"),
            ("ReplaceThumbnailImages", Connection.IsJellyfin ? null : mode != "scan" && replaceThumbnails ? "true" : "false")), null, AdministrationContext, token);
    }

    public Task<JsonObject> GetLibraryNamedConfigurationAsync(string key, CancellationToken token) =>
        http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, $"System/Configuration/{LibraryId(key)}"), AdministrationContext, token);

    public Task SaveLibraryNamedConfigurationAsync(string key, JsonObject configuration, CancellationToken token) =>
        http.PostAsync(EmbyUrl.Combine(ApiBase, $"System/Configuration/{LibraryId(key)}"), configuration, AdministrationContext, token);

    public Task<JsonObject> GetServerDirectoryBrowserAsync(CancellationToken token) =>
        http.GetJsonAsync<JsonObject>(EmbyUrl.Combine(ApiBase, "Environment/DefaultDirectoryBrowser"), AdministrationContext, token);

    public async Task<string> GetServerParentDirectoryAsync(string path, CancellationToken token)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var bytes = await http.GetBytesAsync(EmbyUrl.Combine(ApiBase, "Environment/ParentPath", ("Path", path)),
            AdministrationContext, token).ConfigureAwait(false);
        return Connection.IsJellyfin ? System.Text.Json.JsonSerializer.Deserialize<string>(bytes, EmbyHttp.Json) ?? ""
            : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public Task<JsonArray> GetServerDirectoriesAsync(string path, string username, string password, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path)) return http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Environment/Drives"), AdministrationContext, token);
        if (Connection.IsJellyfin && (path == "Network" || username.Length > 0 || password.Length > 0))
            throw new NotSupportedException("Jellyfin 仅支持浏览服务器已挂载的目录。");
        if (path == "Network") return http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Environment/NetworkDevices"), AdministrationContext, token);
        if (Connection.IsJellyfin)
            return http.GetJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Environment/DirectoryContents",
                ("path", path), ("includeFiles", "false"), ("includeDirectories", "true")), AdministrationContext, token);
        return http.PostJsonAsync<JsonArray>(EmbyUrl.Combine(ApiBase, "Environment/DirectoryContents",
            ("Path", path), ("IncludeFiles", "false"), ("IncludeDirectories", "true")), new { Username = username, Password = password }, AdministrationContext, token);
    }

    public Task ValidateServerDirectoryAsync(string path, string username, string password, bool writable, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Connection.IsJellyfin)
        {
            if (username.Length > 0 || password.Length > 0) throw new NotSupportedException("Jellyfin 不接收目录访问凭据。");
            return http.PostAsync(EmbyUrl.Combine(ApiBase, "Environment/ValidatePath"),
                new { Path = path, ValidateWritable = writable, IsFile = false }, AdministrationContext, token);
        }
        return http.PostAsync(EmbyUrl.Combine(ApiBase, "Environment/ValidatePath", ("Path", path)),
            new { ValidateWriteable = writable, IsFile = false, Username = username, Password = password }, AdministrationContext, token);
    }

    private static string LibraryId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or ".." || id.IndexOfAny(['/', '\\', '?', '#']) >= 0) throw new ArgumentException("媒体库或任务标识无效。");
        return Uri.EscapeDataString(id);
    }
}
