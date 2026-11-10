namespace Momoka.Emby;

/// <summary>Emby / Jellyfin 的 MediaUrl 预告字段；地址只用于打开网页，不携带媒体服务器请求头。</summary>
public sealed class MediaTrailerUrl
{
    public string? Url { get; set; }

    public string? Name { get; set; }
}
