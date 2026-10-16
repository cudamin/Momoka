using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class FnNasChoice(string id, string name, bool isDirectory = true) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool IsDirectory { get; } = isDirectory;
    [ObservableProperty] public partial bool Selected { get; set; }
}

internal static class FnNasJson
{
    internal static string LanguageName(string code) => code switch { "any" => "跟随服务器", "zh-CN" => "简体中文", "zh-TW" => "繁體中文", "en" => "English", _ => code };
    internal static string Text(JsonNode? node, string key, string fallback = "") => node?[key]?.ToString() ?? fallback;
    internal static int Number(JsonNode? node, string key, int fallback = 0) => int.TryParse(Text(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    internal static bool Flag(JsonNode? node, string key) => Text(node, key) is "1" or "true";
    internal static JsonObject Copy(JsonObject node) => (JsonObject)node.DeepClone();
    internal static string[] Strings(JsonNode? node) => node is JsonArray list ? list.Select(value => value?.ToString() ?? "").Where(value => value.Length > 0).ToArray() : [];
    internal static JsonArray Array(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
    internal static string Time(JsonNode? node, string key)
    {
        var text = Text(node, key);
        if (long.TryParse(text, CultureInfo.InvariantCulture, out var number) && number > 0)
        {
            try { return (number > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number)).ToLocalTime().ToString("g", CultureInfo.CurrentCulture); }
            catch (ArgumentOutOfRangeException) { }
        }
        return text.Length == 0 || text == "0" ? "暂无记录" : text;
    }
}

public sealed class FnNasLibraryRow(JsonObject library)
{
    internal JsonObject Document { get; } = FnNasJson.Copy(library);
    public string Id { get; } = FnNasJson.Text(library, "guid");
    public string Name { get; } = FnNasJson.Text(library, "name");
    public string Category { get; } = FnNasJson.Text(library, "category");
    public string CategoryName => Category switch { "Movie" => "电影", "TV" => "电视节目", "Mix" => "混合影片", "IPTV" => "电视直播", "Others" => "其他视频", _ => Category };
    public string Directories { get; } = string.Join("\n", FnNasJson.Strings(library["dir_list"]));
    public string Updated { get; } = "最近更新：" + FnNasJson.Time(library, "last_update_time");
    public Visibility ScanVisibility => Category == "IPTV" ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class FnNasTaskRow(JsonObject task)
{
    public Visibility NoteVisibility => SettingRow.NotesVisibility;
    public string Name { get; } = FnNasJson.Text(task, "name") switch { "scrap-item" => "扫描媒体库文件", "extra-subtitle" => "提取内置字幕", var name => name };
    public string Description { get; } = FnNasJson.Text(task, "name") switch { "scrap-item" => "检查媒体文件的增删变更", "extra-subtitle" => "提取特效字幕和高画质文件中的图形字幕", _ => "" };
    public string Started { get; } = "上次开始：" + FnNasJson.Time(task, "last_run_start_time");
    public string Finished { get; } = "上次结束：" + FnNasJson.Time(task, "last_run_end_time");
}

public sealed class FnNasUserRow(JsonObject user, IReadOnlyDictionary<string, string> libraries)
{
    internal JsonObject Document { get; } = FnNasJson.Copy(user);
    public string Id { get; } = FnNasJson.Text(user, "guid");
    public string Name { get; } = FnNasJson.Text(user, "username");
    public bool IsAdministrator { get; } = FnNasJson.Flag(user, "is_admin");
    public bool IsBanned { get; } = FnNasJson.Flag(user, "is_banned");
    public string LastLogin { get; } = FnNasJson.Time(user, "last_login_time");
    public string Role { get; } = FnNasJson.Flag(user, "is_admin") ? "管理员" : FnNasJson.Flag(user, "is_banned") ? "已锁定" : "普通用户";
    public string NasAccount { get; } = user["sources"] is JsonArray sources
        ? string.Join("、", sources.OfType<JsonObject>().Where(source => FnNasJson.Text(source, "source") == "Trim-NAS").Select(source => FnNasJson.Text(source, "source_name"))) : "";
    public string Access { get; } = FnNasJson.Flag(user, "is_admin") ? "全部媒体库" : FnNasJson.Number(user, "media_permission") switch
    {
        2 => "全部媒体库（含以后新增）",
        1 => string.Join("、", PermissionIds(user).Select(id => libraries.GetValueOrDefault(id, "不可用媒体库"))),
        _ => "无媒体库访问权限"
    };
    internal static string[] PermissionIds(JsonObject user) => user["mediadb_list"] is JsonArray list
        ? list.Select(value => value is JsonObject obj ? FnNasJson.Text(obj, "guid") : value?.ToString() ?? "").Where(value => value.Length > 0).ToArray() : [];
}
