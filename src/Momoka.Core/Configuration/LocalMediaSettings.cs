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
}
