namespace Momoka.Configuration;

/// <summary>
/// 设置 → 关于 → 网络 那张卡。目前只有代理服务器一行，但它管着两件出门的事：本机整理向 TMDB 刮削
/// （<c>TmdbClient</c>），以及详情页的 YouTube 预告（共享的 WebView2 配置）。
/// <para>
/// 这是本机的网络配置，不是可分享的偏好内容 —— 不过它既没有地址也没有凭据，所以仍跟着设置页那套
/// 备份/恢复默认走（见 <see cref="SettingsPreferences"/>）。空串是「不设置」，两个消费者都回到系统代理。
/// </para>
/// </summary>
public sealed class NetworkSettings
{
    /// <summary>
    /// 代理服务器地址，写法见 <see cref="Infrastructure.ProxyEndpoint"/>；空串表示不设置、跟随系统。
    /// </summary>
    public string ProxyServer { get; set; } = "";
}
