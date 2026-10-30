namespace Momoka.Emby;

/// <summary>图片编辑器的请求尺寸和远程预览回退；预览只读，原始候选仍用于正式换图。</summary>
public static class ArtworkPreview
{
    /// <summary>单集 Primary 是一帧 16:9 剧照；不改变剧、电影或其他图种的公共目录。</summary>
    public static ArtworkKind KindFor(EmbyItem item, ArtworkKind kind)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Type == EmbyItemType.Episode && kind.ImageType == "Primary"
            ? kind with { Ratio = 16d / 9 } : kind;
    }

    /// <summary>服务器缩图使用物理像素，WinUI 解码和画框仍使用逻辑像素。</summary>
    public static int PixelWidth(int logicalWidth, double rasterizationScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalWidth);
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale));
        return (int)Math.Clamp(Math.Ceiling(logicalWidth * rasterizationScale), 1, 16384);
    }

    /// <summary>缩略图确实无法使用时才尝试原图，沿用图片候选的取消、身份和错误边界。</summary>
    public static Task<TImage?> RemoteAsync<TImage>(RemoteImageInfo info,
        Func<string, CancellationToken, Task<TImage?>> load, CancellationToken cancellationToken)
        where TImage : class
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(load);
        var addresses = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(info.ThumbnailUrl)) addresses.Add(info.ThumbnailUrl);
        if (!string.IsNullOrWhiteSpace(info.Url) && !addresses.Contains(info.Url, StringComparer.Ordinal))
            addresses.Add(info.Url);
        return ArtworkCandidates.FirstAsync(addresses, load, cancellationToken);
    }
}
