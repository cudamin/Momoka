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
}
