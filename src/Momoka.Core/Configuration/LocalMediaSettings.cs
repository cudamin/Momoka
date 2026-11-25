namespace Momoka.Configuration;

/// <summary>本机路径和识别连接属于这台电脑，不导出到可分享的偏好配置。</summary>
public sealed class LocalMediaSettings
{
    /// <summary>
    /// 旧版单目录配置留下的源目录。只用于把一次都没有保存过媒体库的旧设置迁进
    /// <see cref="Libraries"/>；新界面不再读写这一项。
    /// </summary>
    public string SourceDirectory { get; set; } = "";

    public string Provider { get; set; } = "momoka";
    public bool Recursive { get; set; } = true;

    /// <summary>刮削出的 NFO、海报与剧照放在哪里；空串表示用 <c>%LOCALAPPDATA%\Momoka\metadata</c>。</summary>
    public string MetadataDirectory { get; set; } = "";

    public string ProtectedTmdbToken { get; set; } = "";
    public List<LocalMediaLibrarySettings> Libraries { get; set; } = [];
    public string SelectedLibraryId { get; set; } = "";
    public bool LibrariesMigrated { get; set; }
}

/// <summary>一个目录就是一个本地媒体库；文件保持原位，不建立链接也不改名。</summary>
public sealed class LocalMediaLibrarySettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "本地媒体库";
    public string SourceDirectory { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool MonitorEnabled { get; set; }

    public LocalMediaLibrarySettings Copy() => new()
    {
        Id = Id,
        Name = Name,
        SourceDirectory = SourceDirectory,
        Recursive = Recursive,
        MonitorEnabled = MonitorEnabled
    };
}
