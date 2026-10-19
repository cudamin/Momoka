namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>
    /// 管理员左栏里由插件贡献的页面。两家网页端问的是同一条 <c>getConfigurationPages</c>，只要主菜单
    /// 里那几项，但参数形状不同：Emby 还带页面类型和当前用户（它的 javascript 客户端如此），
    /// Jellyfin 只传 <c>enableInMainMenu</c>（SDK 的 generated client 如此）。只读，不改服务器设置。
    /// </summary>
    public async Task<List<ServerSettingsPluginPage>> GetServerSettingsPagesAsync(CancellationToken cancellationToken)
    {
        if (Connection.ServerKind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin))
            throw new NotSupportedException("此服务器没有插件管理页");

        var pages = await http.GetJsonAsync<List<ServerSettingsPluginPage>>(
            Connection.IsJellyfin
                ? Url("web/ConfigurationPages", ("enableInMainMenu", "true"))
                : Url("web/ConfigurationPages",
                    ("PageType", "PluginConfiguration"),
                    ("UserId", Connection.UserId),
                    ("EnableInMainMenu", "true")),
            Context, cancellationToken).ConfigureAwait(false);
        return pages;
    }
}
