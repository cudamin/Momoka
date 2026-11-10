namespace Momoka.Configuration;

/// <summary>本机路径和识别连接属于这台电脑，不导出到可分享的偏好配置。</summary>
public sealed class LocalMediaSettings
{
    public string SourceDirectory { get; set; } = "";
    public string TargetDirectory { get; set; } = "";
    public string Method { get; set; } = "hardlink";
    public string Provider { get; set; } = "momoka";
    public bool Recursive { get; set; } = true;
    public string ProtectedTmdbToken { get; set; } = "";
    public List<LocalMediaLibrarySettings> Libraries { get; set; } = [];
    public string SelectedLibraryId { get; set; } = "";
    public bool LibrariesMigrated { get; set; }
}

public sealed class LocalMediaLibrarySettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "本地媒体库";
    public string SourceDirectory { get; set; } = "";
    public string TargetDirectory { get; set; } = "";
    public string Method { get; set; } = "hardlink";
    public string Provider { get; set; } = "momoka";
    public bool Recursive { get; set; } = true;
    public bool MonitorEnabled { get; set; }

    public LocalMediaLibrarySettings Copy() => new()
    {
        Id = Id,
        Name = Name,
        SourceDirectory = SourceDirectory,
        TargetDirectory = TargetDirectory,
        Method = Method,
        Provider = Provider,
        Recursive = Recursive,
        MonitorEnabled = MonitorEnabled
    };
}
