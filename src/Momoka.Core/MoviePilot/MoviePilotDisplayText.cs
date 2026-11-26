using System.Text.RegularExpressions;

namespace Momoka.MoviePilot;

/// <summary>本地文件和云存储的对象键都可展示。带链接、认证字段或控制字符的值整项隐藏，原值只用于身份匹配。</summary>
public static partial class MoviePilotDisplayText
{
    public static string Path(string value) => CanDisplay(value) ? value : "[路径已隐藏]";

    public static string Label(string value) => CanDisplay(value) ? value : "[内容已隐藏]";

    public static bool CanDisplay(string value)
    {
        if (value.Length > 8192) return false;
        // 只检查解码副本，普通含百分号的文件名仍按服务器拼写显示。
        for (var depth = 0; depth < 4; depth++)
        {
            if (value.Any(char.IsControl) || SensitiveMaterial().IsMatch(value)) return false;
            if (!value.Contains('%')) return true;
            var decoded = Uri.UnescapeDataString(value);
            if (decoded == value) return true;
            value = decoded;
        }
        return false;
    }

    [GeneratedRegex(
        @"://|(?:^|[/\\])[/\\][^/\\\s]*@|[?&][^/\\\s=&#?]+=[^/\\\s]+|(?<![\w-])(?:authorization|cookie)\s*[:=]\s*\S+|(?<![\w-])bearer\s+\S+|(?<![\w-])(?:pass(?:word|key)?|(?:access|api|auth|refresh)[_-]?token|token|api[_-]?key|secret|jwt)\s*=\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveMaterial();
}
