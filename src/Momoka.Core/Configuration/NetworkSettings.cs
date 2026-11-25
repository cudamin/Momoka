namespace Momoka.Configuration;

/// <summary>
/// 设置 → 关于 → 网络中的代理配置，供 YouTube 预告、AniBT 和 Bangumi 查询使用。
/// <see cref="SettingsPreferences"/> 负责备份与恢复。
/// </summary>
public sealed class NetworkSettings
{
    /// <summary>
    /// 代理服务器地址，写法见 <see cref="Infrastructure.ProxyEndpoint"/>；空串表示不设置、跟随系统。
    /// </summary>
    public string ProxyServer { get; set; } = "";
}
