namespace Momoka.Emby;

/// <summary>多账号主页上的服务器名前缀／后缀；账号管理仍使用完整身份。</summary>
public static class SourceCaption
{
    public static string Append(string? content, EmbySessionScope? source, bool showSource = true) =>
        string.Join(" · ", new[] { content, showSource ? source?.ServerName : null }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// 把服务器名放在内容前面：多账号主页的「最近添加」货架标题和媒体库名称要能一眼看出这一排／这一格属于哪台
    /// 服务器。空内容、没有来源和不显示时都不留下分隔符。
    /// </summary>
    public static string Prefix(string? content, EmbySessionScope? source, bool showSource = true) =>
        string.Join(" · ", new[] { showSource ? source?.ServerName : null, content }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}
