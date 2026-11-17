namespace Momoka.Emby;

/// <summary>多账号主页与侧边栏上的账号名前缀／服务器名后缀；账号管理仍使用完整身份。</summary>
public static class SourceCaption
{
    public static string Append(string? content, EmbySessionScope? source, bool showSource = true) =>
        string.Join(" · ", new[] { content, showSource ? source?.ServerName : null }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// 把账号用户名放在内容前面：多账号主页的「最近添加」货架标题、媒体库名称和侧边栏库项要能一眼看出
    /// 这一排／这一格属于哪个账号（用户令 2026-11-16，此前写服务器名）。空内容、没有来源和不显示时都不留下分隔符。
    /// </summary>
    public static string Prefix(string? content, EmbySessionScope? source, bool showSource = true) =>
        string.Join(" · ", new[] { showSource ? source?.UserName : null, content }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// 主页该不该在库名前面写账号用户名。只数会带来影视库的来源：飞牛音乐只交出「音乐」这一格，
    /// 不跟影视库撞名 —— 用户令 2026-11-17「只登录了一个库+飞牛音乐的情况下，不需要给首页的库改名」，
    /// 用户名在那里只会变成噪声。两个及以上其余在线来源才写（用户令 2026-11-16）；一个来源时多出来的
    /// 用户名不区分任何东西。
    /// </summary>
    public static bool ShowsAccountNames(IEnumerable<EmbySessionScope> sources) =>
        sources.Count(source => !source.Connection.IsFnMusic) > 1;
}
