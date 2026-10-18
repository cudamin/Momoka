namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>
    /// 管理员左栏里由插件贡献的页面。官方网页端 <c>getConfigurationPages</c> 问的就是这一条，参数也照它：
    /// <c>PageType=PluginConfiguration</c>、当前用户，以及只看主菜单。只读，不改服务器上的任何设置。
    /// <para>
    /// 服务器不是 Emby 时没有这套页面（Jellyfin 的网页端有自己的菜单），所以这里只走 Emby。
    /// </para>
    /// </summary>
    public async Task<List<ServerSettingsPluginPage>> GetServerSettingsPagesAsync(CancellationToken cancellationToken)
    {
        RequireEmby("服务器设置");

        var pages = await http.GetJsonAsync<List<ServerSettingsPluginPage>>(
            Url("web/ConfigurationPages",
                ("PageType", "PluginConfiguration"),
                ("UserId", Connection.UserId),
                ("EnableInMainMenu", "true")),
            Context, cancellationToken).ConfigureAwait(false);
        return pages;
    }
}
