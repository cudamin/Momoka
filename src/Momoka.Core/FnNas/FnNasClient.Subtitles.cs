using System.Text.Json.Nodes;

namespace Momoka.FnNas;

public sealed partial class FnNasClient
{
    /// <summary>Official 0.9.8 HB: language is the sys/config language code, not an ISO-639-2 track label.</summary>
    public async Task<IReadOnlyList<FnNasSubtitleCandidate>> SearchSubtitlesAsync(FnNasConnection c, string mediaGuid,
        string language, CancellationToken ct = default)
    {
        _ = FnNasEndpoint.Segment(mediaGuid);
        if (string.IsNullOrWhiteSpace(language) || language.Length > 24 || language.Any(char.IsControl))
            throw new ArgumentException("请选择字幕语言", nameof(language));
        var data = await ObjectAsync(c, HttpMethod.Post, "api/v1/subtitle/search",
            new JsonObject { ["lan"] = language, ["media_guid"] = mediaGuid }, ct).ConfigureAwait(false);
        if (data["subtitles"] is not JsonArray subtitles || subtitles.Any(value => value is not JsonObject))
            throw new FnNasApiException("飞牛影视返回了无效的字幕搜索结果");
        return subtitles.OfType<JsonObject>().Select(value => new FnNasSubtitleCandidate(
                FnNasMediaAdapter.Text(value, "trim_id"), FnNasMediaAdapter.Text(value, "filename"),
                FnNasMediaAdapter.Text(value, "format"), FnNasMediaAdapter.Text(value, "download")))
            .Where(candidate => candidate.TrimId.Length > 0).DistinctBy(candidate => candidate.TrimId).ToArray();
    }

    /// <summary>Uxe supplies sync_download=1 and the provider trim_id; code=0 is the write acknowledgement.</summary>
    public Task DownloadSubtitleAsync(FnNasConnection c, string mediaGuid, string trimId,
        Action confirmed, CancellationToken ct = default, Action? dispatched = null)
    {
        _ = FnNasEndpoint.Segment(mediaGuid);
        _ = FnNasEndpoint.Segment(trimId);
        return SubtitleActionAsync(c, HttpMethod.Post, "api/v1/subtitle/download", new JsonObject
        { ["media_guid"] = mediaGuid, ["trim_id"] = trimId, ["sync_download"] = 1 }, confirmed, ct, dispatched);
    }

    public Task DeleteSubtitleAsync(FnNasConnection c, string subtitleGuid, Action confirmed, CancellationToken ct = default,
        Action? dispatched = null)
    {
        _ = FnNasEndpoint.Segment(subtitleGuid);
        return SubtitleActionAsync(c, HttpMethod.Delete, "api/v1/subtitle/del",
            new JsonObject { ["subtitle_guid"] = subtitleGuid }, confirmed, ct, dispatched);
    }

    /// <summary>Native upload hook sends one unchanged file in a multipart field named file, without authx.</summary>
    public Task UploadSubtitleAsync(FnNasConnection c, string mediaGuid, FnNasSubtitleUpload upload,
        Action confirmed, CancellationToken ct = default, Action? dispatched = null)
    {
        _ = FnNasEndpoint.Segment(mediaGuid);
        FnNasSubtitleInput.ValidateUpload(upload);
        c.ThrowIfNotCurrent();
        ct.ThrowIfCancellationRequested();
        return http.UploadConfirmedAsync(FnNasEndpoint.Api(c.ApiBase, "api/v1/subtitle/upload/" + FnNasEndpoint.Segment(mediaGuid)),
            upload.Bytes, upload.FileName, c.AccessToken, confirmed, ct, () =>
            {
                c.ThrowIfNotCurrent();
                dispatched?.Invoke();
            });
    }

    /// <summary>WB and kxe keep NAS-file selection separate from ordinary search/upload.</summary>
    public async Task MarkSubtitleFilesAsync(FnNasConnection c, string mediaGuid, IReadOnlyList<string> filePaths,
        Action confirmed, CancellationToken ct = default, Action? dispatched = null)
    {
        _ = FnNasEndpoint.Segment(mediaGuid);
        if (filePaths.Count is 0 or > 20) throw new ArgumentException("请选择 1 至 20 个服务器字幕文件");
        foreach (var path in filePaths) FnNasSubtitleInput.ValidateServerPath(path);
        await RequireAdminAsync(c, ct).ConfigureAwait(false);
        await SubtitleActionAsync(c, HttpMethod.Put, "api/v1/subtitle/mark", new JsonObject
        { ["media_guid"] = mediaGuid, ["filepaths"] = Strings(filePaths.Distinct(StringComparer.Ordinal).ToArray()) }, confirmed, ct, dispatched).ConfigureAwait(false);
    }

    /// <summary>The SDK's raw dl route is fixed to the subtitle GUID and has no token in its URL.</summary>
    public static Uri SubtitleUrl(Uri apiBase, string subtitleGuid) =>
        FnNasEndpoint.Api(apiBase, "api/v1/subtitle/dl/" + FnNasEndpoint.Segment(subtitleGuid));

    private Task SubtitleActionAsync(FnNasConnection c, HttpMethod method, string path, JsonObject body,
        Action confirmed, CancellationToken ct, Action? dispatched = null)
    {
        c.ThrowIfNotCurrent();
        ct.ThrowIfCancellationRequested();
        return http.SendConfirmedActionAsync(method, FnNasEndpoint.Api(c.ApiBase, path), body, c.AccessToken, confirmed, ct, () =>
        {
            c.ThrowIfNotCurrent();
            dispatched?.Invoke();
        });
    }
}
