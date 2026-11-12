namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>Jellyfin 10.10.7/12.4 的原生上传；versionItemId 必须是已验证文件对应的版本 GUID。</summary>
    public Task UploadSubtitleAsync(string versionItemId, ServerSubtitleUpload upload, CancellationToken cancellationToken)
        => UploadSubtitleAsync(versionItemId, upload, cancellationToken, null);

    internal void ValidateSubtitleUpload(string versionItemId, ServerSubtitleUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (!Connection.IsJellyfin) throw new NotSupportedException("当前服务器不支持此原生字幕上传。");
        if (Connection.CanUploadSubtitles != true) throw new UnauthorizedAccessException("当前账号没有字幕上传权限。");
        if (!Guid.TryParse(versionItemId, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("字幕上传缺少有效的版本条目。");
    }

    internal Task UploadSubtitleAsync(string versionItemId, ServerSubtitleUpload upload, CancellationToken cancellationToken, Action? confirmed)
    {
        ValidateSubtitleUpload(versionItemId, upload);
        return http.PostNoContentAsync(Url($"Videos/{SubtitleItemId(versionItemId)}/Subtitles"), new
        {
            upload.File.Data,
            upload.Language,
            upload.IsForced,
            upload.File.Format,
            upload.IsHearingImpaired
        }, Context, cancellationToken, confirmed);
    }
}
