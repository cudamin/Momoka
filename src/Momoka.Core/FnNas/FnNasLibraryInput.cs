using System.Text;
using System.Text.Json.Nodes;

namespace Momoka.FnNas;

/// <summary>Limits in the official library form; NAS and URL file sizes remain server-validated.</summary>
public static class FnNasLibraryInput
{
    public const int MaximumUploadBytes = 10 * 1024 * 1024;
    public const int MaximumPasteBytes = 2 * 1024 * 1024;
    public const int MaximumLineBytes = 8 * 1024;
    public const int MaximumPasteLines = 50000;
    public const int MaximumUploadLines = 100000;

    /// <summary>
    /// An existing IPTV document explicitly naming a stored non-URL source may retain it when renamed.
    /// Servers need not return the original pasted bytes or an already-consumed upload temp id.
    /// A newly selected category or a missing/unknown source descriptor cannot prove a stored playlist.
    /// </summary>
    public static bool HasStoredIptvSource(JsonObject? original) =>
        original is not null && Text(original, "category") == "IPTV"
        && original["iptv"] is JsonObject source
        && Text(source, "source_type") is "text" or "local_file" or "upload";

    public static void Validate(JsonObject draft, bool editing)
    {
        var name = Text(draft, "name").Trim();
        if (name.Length is < 1 or > 32) throw new FnNasApiException("媒体库名称应为 1 至 32 个字符");
        var category = Text(draft, "category");
        if (category is not ("Movie" or "TV" or "Mix" or "Others" or "IPTV"))
            throw new FnNasApiException("请选择有效的飞牛影视媒体库类型");
        if (category != "IPTV")
        {
            if (draft["dir_list"] is not JsonArray directories || directories.Count == 0 ||
                directories.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out var path) || string.IsNullOrWhiteSpace(path)))
                throw new FnNasApiException("请选择至少一个媒体目录");
            return;
        }
        if (draft["iptv"] is not JsonObject source)
        {
            if (editing) return; // Renaming an existing file/text source must not overwrite its source.
            throw new FnNasApiException("请填写直播源");
        }
        switch (Text(source, "source_type"))
        {
            case "url":
                if (string.IsNullOrWhiteSpace(Text(source, "source_url"))) throw new FnNasApiException("请填写直播源地址");
                if (source["refresh_interval"] is not JsonValue interval || !interval.TryGetValue<int>(out var seconds) ||
                    seconds is not (0 or 3600 or 10800 or 21600 or 43200))
                    throw new FnNasApiException("请选择有效的直播源刷新间隔");
                break;
            case "text":
                var text = Text(source, "content");
                if (string.IsNullOrWhiteSpace(text)) throw new FnNasApiException("请粘贴直播源内容");
                ValidateContent(Encoding.UTF8.GetBytes(text), MaximumPasteBytes, MaximumPasteLines);
                break;
            case "local_file":
                if (string.IsNullOrWhiteSpace(Text(source, "local_file_path"))) throw new FnNasApiException("请选择 NAS 上的直播源文件");
                break;
            case "upload":
                if (string.IsNullOrWhiteSpace(Text(source, "temp_file_id"))) throw new FnNasApiException("请先上传直播源文件");
                break;
            default:
                throw new FnNasApiException("请选择有效的直播源导入方式");
        }
    }

    public static void ValidateUpload(byte[] bytes, string filename)
    {
        if (filename.IndexOfAny(['/', '\\', '"']) >= 0 || filename.Any(char.IsControl) || filename.Length > 255 ||
            Path.GetExtension(filename).ToLowerInvariant() is not (".txt" or ".m3u" or ".m3u8"))
            throw new FnNasApiException("直播源文件只支持 txt、m3u 和 m3u8");
        if (bytes.Length == 0) throw new FnNasApiException("直播源文件不能为空");
        ValidateContent(bytes, MaximumUploadBytes, MaximumUploadLines);
    }

    private static void ValidateContent(byte[] bytes, int maximumBytes, int maximumLines)
    {
        if (bytes.Length > maximumBytes) throw new FnNasApiException($"直播源大小不能超过 {maximumBytes / 1024 / 1024} MB");
        var lines = 0;
        // The web form counts non-blank lines. Use the actual UTF8 byte count for the line limit.
        using var reader = new StringReader(Encoding.UTF8.GetString(bytes));
        while (reader.ReadLine() is { } line)
        {
            if (Encoding.UTF8.GetByteCount(line) > MaximumLineBytes) throw new FnNasApiException("直播源每行不能超过 8 KB");
            if (!string.IsNullOrWhiteSpace(line) && ++lines > maximumLines)
                throw new FnNasApiException($"直播源不能超过 {maximumLines} 个非空行");
        }
    }

    private static string Text(JsonObject value, string key) =>
        value[key] is JsonValue item && item.TryGetValue<string>(out var text) ? text : "";
}
