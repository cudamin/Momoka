namespace Momoka.Emby;

/// <summary>版本选择中的服务器后缀；账号管理仍使用完整身份。</summary>
public static class SourceCaption
{
    public static string Append(string? content, EmbySessionScope? source, bool showSource = true) =>
        string.Join(" · ", new[] { content, showSource ? source?.ServerName : null }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}
