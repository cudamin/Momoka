using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnNas;

/// <summary>The search provider's identifier is unrelated to a local subtitle GUID or stream index.</summary>
public sealed record FnNasSubtitleCandidate(string TrimId, string FileName, string Format, string Downloads)
{
    public bool CanDownload => FnNasSubtitleInput.IsSearchFormat(Format);
    public string Description => string.Join(" · ", new[] { Format.ToUpperInvariant(), Downloads.Length > 0 ? $"下载 {Downloads}" : "" }
        .Where(value => value.Length > 0));
}

/// <summary>Native sidecar identity; all listed fields participate in the pre-delete comparison.</summary>
public sealed record FnNasSubtitleTrack(string Guid, string MediaGuid, string Path, string FileName,
    string Format, string Language, string Title, string TrimId, int? Index, bool IsExternal, bool ExtraFile, string Url)
{
    public string Identity { get; init; } = "";
    // Official SubtitleCard only offers RemoveButton for isExternal and hides it for a URL track.
    public bool CanDelete => IsExternal && Guid.Length > 0 && Url.Length == 0;
    public string Label => FileName.Length > 0 ? FileName : Path.Length > 0 ? System.IO.Path.GetFileName(Path)
        : Title.Length > 0 ? Title : $"{Language} · {Format.ToUpperInvariant()}";

    internal static FnNasSubtitleTrack From(JsonObject value) => new(
        FnNasMediaAdapter.Text(value, "guid"), FnNasMediaAdapter.Text(value, "media_guid"),
        FnNasMediaAdapter.Text(value, "path"), FnNasMediaAdapter.Text(value, "filename") is { Length: > 0 } name
            ? name : FnNasMediaAdapter.Text(value, "file_name"),
        FnNasMediaAdapter.Text(value, "format") is { Length: > 0 } format ? format : FnNasMediaAdapter.Text(value, "codec_name"),
        FnNasMediaAdapter.Text(value, "language"), FnNasMediaAdapter.Text(value, "title"), FnNasMediaAdapter.Text(value, "trim_id"),
        FnNasMediaAdapter.Integer(value, "index"), FnNasMediaAdapter.Flag(value, "is_external"),
        FnNasMediaAdapter.Flag(value, "extra_file"), FnNasMediaAdapter.Text(value, "url"))
        { Identity = Canonical(value)!.ToJsonString(FnNasHttp.Json) };

    private static JsonNode? Canonical(JsonNode? value) => value switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Canonical(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonical).ToArray()),
        _ => value?.DeepClone()
    };
}

public sealed record FnNasSubtitleSnapshot(string MediaGuid, string Path, IReadOnlyList<FnNasSubtitleTrack> Tracks)
{
    public IReadOnlyList<FnNasSubtitleTrack> ExternalTracks => Tracks.Where(track => track.IsExternal || track.ExtraFile).ToArray();

    internal static FnNasSubtitleSnapshot From(JsonObject streams, string mediaGuid, string path)
    {
        if (streams["files"] is not JsonArray files || files.Any(file => file is not JsonObject))
            throw new FnNasApiException("飞牛影视未返回完整的媒体版本列表");
        var matching = files.OfType<JsonObject>().Where(file => FnNasMediaAdapter.Text(file, "guid") == mediaGuid).ToArray();
        if (matching.Length != 1 || FnNasMediaAdapter.Text(matching[0], "path") != path)
            throw new InvalidOperationException("媒体文件已变化，请关闭字幕管理后重新打开");
        if (streams["subtitle_streams"] is not JsonArray tracks || tracks.Any(track => track is not JsonObject))
            throw new FnNasApiException("飞牛影视未返回完整的字幕列表");
        return new(mediaGuid, path, tracks.OfType<JsonObject>().Select(FnNasSubtitleTrack.From)
            .Where(track => track.MediaGuid == mediaGuid).OrderBy(track => track.Guid, StringComparer.Ordinal)
            .ThenBy(track => track.Index).ToArray());
    }
}

public sealed record FnNasSubtitleUpload(string FileName, byte[] Bytes);

public static class FnNasSubtitleInput
{
    public const int MaxUploadBytes = 32 * 1024 * 1024;
    public static readonly IReadOnlyList<string> Extensions = [".srt", ".sub", ".ssa", ".ass", ".vtt", ".sup"];

    public static bool IsSearchFormat(string format) => format.ToLowerInvariant() is "srt" or "vtt" or "ass" or "ssa";

    public static void ValidateUpload(FnNasSubtitleUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (string.IsNullOrWhiteSpace(upload.FileName) || upload.FileName.Any(char.IsControl)
            || upload.FileName.IndexOfAny(['/', '\\', ':']) >= 0
            || !Extensions.Contains(System.IO.Path.GetExtension(upload.FileName), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("请选择 SRT、SUB、SSA、ASS、VTT 或 SUP 字幕文件");
        if (upload.Bytes is null || upload.Bytes.Length is 0 or > MaxUploadBytes)
            throw new ArgumentException("字幕文件须大于 0 且不超过 32 MiB");
    }

    public static void ValidateServerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.Any(char.IsControl)
            || path.Contains('\\') || path.Split('/').Any(segment => segment is "." or "..")
            || !Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("请填写服务器上字幕文件的完整路径");
    }
}

/// <summary>The server may have accepted this write. Recovery may read, but must never replay it.</summary>
public sealed class FnNasSubtitleWriteUnconfirmedException(Exception inner)
    : Exception("字幕写入未收到可确认的回执，请只刷新列表核对，关闭后重新打开再操作", inner);
