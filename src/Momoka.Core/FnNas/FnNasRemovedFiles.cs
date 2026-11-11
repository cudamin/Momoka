using System.Text.Json.Nodes;

namespace Momoka.FnNas;

/// <summary>官方 fileViewAdmin/list 的一行，guid 是目录导航身份，恢复使用原 path。</summary>
public sealed record FnNasRemovedFile(string Guid, string MediaGuid, string Name, string Path, bool IsFolder, bool IsRemoved)
{
    public bool CanBrowse => IsFolder && !IsRemoved && !string.IsNullOrWhiteSpace(Guid);
    public bool CanRestore => IsRemoved && !string.IsNullOrWhiteSpace(Path);
    public string Status => IsRemoved ? IsFolder ? "已移除文件夹" : "已移除文件" : "打开文件夹";

    internal static FnNasRemovedFile From(JsonObject value) => new(
        Text(value, "guid"), Text(value, "media_guid"), Text(value, "file_name"), Text(value, "path"),
        Text(value, "type") == "Directory", value["blacked"] is JsonValue scalar && scalar.TryGetValue<int>(out var number) && number == 1);

    internal static string Text(JsonObject value, string key) => value[key] is JsonValue scalar
        && scalar.TryGetValue<string>(out var text) ? text : "";
}

public sealed record FnNasRemovedFileDirectory(string LibraryName, string Path, IReadOnlyList<FnNasRemovedFile> Entries)
{
    /// <summary>完整原始行只用于已确认恢复的正常态核对，界面仍使用精简Entries。</summary>
    public IReadOnlyList<FnNasRemovedFile> AllEntries { get; init; } = [];
    internal static FnNasRemovedFileDirectory From(JsonObject value)
    {
        if (value["list"] is not JsonArray list || list.Any(entry => entry is not JsonObject))
            throw new FnNasApiException("飞牛影视恢复目录的列表格式不正确");
        var entries = list.Cast<JsonObject>().Select(FnNasRemovedFile.From).ToArray();
        return new(FnNasRemovedFile.Text(value, "mdb_name"), FnNasRemovedFile.Text(value, "dir"),
            entries.Where(entry => entry.CanBrowse || entry.IsRemoved).ToArray())
        { AllEntries = entries };
    }
}
