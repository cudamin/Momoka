using System.Text;
using Momoka.Configuration;

namespace Momoka.Mpv;

/// <summary>
/// 用户配置仍由 mpv 解析。config=no 避免自动载入脚本、input.conf 和观看记录，但也会禁用 ~~ 路径；
/// 因此只在临时副本展开资源根与 include，原文件不改。宿主拥有的选项在所有配置组中都排除。
/// </summary>
public sealed class MpvCustomConfig : IDisposable
{
    private readonly List<string> _copies = [];
    public string FilePath { get; private set; } = "";
    public string SourcePath { get; private set; } = "";

    public static bool IsAvailable(MpvSettings settings) =>
        settings.Backend == MpvBackendKind.BuiltInLibMpv && settings.Pipeline == VideoPipelineKind.Standalone;

    public static string? Resolve(MpvSettings settings)
    {
        if (!IsAvailable(settings) || !settings.CustomConfigEnabled) return null;
        var path = TypedPath.Clean(settings.CustomConfigPath ?? "");
        if (path.Length == 0) throw new InvalidOperationException("已启用自定义 mpv.conf，请在设置 → 播放器中填写配置文件路径。");
        try { return Path.GetFullPath(path, AppContext.BaseDirectory); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("自定义 mpv.conf 路径无效，请检查设置 → 播放器中的路径。", error);
        }
    }

    public static MpvCustomConfig Prepare(string source)
    {
        var result = new MpvCustomConfig();
        try
        {
            var fullPath = Path.GetFullPath(source);
            result.SourcePath = fullPath;
            result.FilePath = result.Copy(fullPath, Path.GetDirectoryName(fullPath)!, new(StringComparer.OrdinalIgnoreCase));
            return result;
        }
        catch (Exception error)
        {
            result.Dispose();
            throw new InvalidOperationException($"无法读取自定义 mpv.conf：{error.Message}", error);
        }
    }

    private string Copy(string source, string configRoot, HashSet<string> ancestors)
    {
        if (ancestors.Count >= 16 || !ancestors.Add(source))
            throw new InvalidOperationException("配置文件的 include 存在循环或嵌套过深。");
        var text = new StringBuilder();
        foreach (var original in File.ReadLines(source))
        {
            var line = original.TrimStart();
            if (line.Length == 0 || line[0] is '#' or '[')
            {
                text.AppendLine(original);
                continue;
            }
            var separator = line.IndexOfAny(['=', ' ', '\t']);
            var name = (separator < 0 ? line : line[..separator]).TrimStart('-');
            if (IsHostOption(name)) continue;
            line = original.Replace("~~/", configRoot.Replace('\\', '/') + "/", StringComparison.Ordinal)
                .Replace("~~\\", configRoot.Replace('\\', '/') + "/", StringComparison.Ordinal);
            if (name.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.TrimStart()[separator..].TrimStart(' ', '\t', '=').Trim();
                if (value.Length == 0) throw new InvalidOperationException("include 缺少文件路径。");
                if (value[0] is '\'' or '"')
                {
                    var end = value.IndexOf(value[0], 1);
                    if (end < 0) throw new InvalidOperationException("include 的路径引号未闭合。");
                    value = value[1..end];
                }
                else value = value.Split('#')[0].Trim();
                var include = Path.GetFullPath(value, Path.GetDirectoryName(source)!);
                line = "include=" + Copy(include, configRoot, ancestors).Replace('\\', '/');
            }
            text.AppendLine(line);
        }
        ancestors.Remove(source);
        var copy = Path.Combine(Path.GetTempPath(), $"momoka-mpv-{Guid.NewGuid():N}.conf");
        _copies.Add(copy);
        File.WriteAllText(copy, text.ToString(), new UTF8Encoding(false));
        return copy;
    }

    internal static bool IsHostOption(string name)
    {
        var key = name.TrimStart('-');
        if (key.StartsWith("no-", StringComparison.OrdinalIgnoreCase)) key = key[3..];
        string[] owned =
        [
            "config", "config-dir", "use-filedir-conf", "input-conf", "input-ipc", "script", "scripts", "load-scripts",
            "log-file", "dump-stats", "terminal", "msg-level", "playlist", "http-header-fields",
            "resume-playback", "save-position-on-quit", "save-watch-history", "watch-later", "watch-history",
            "idle", "keep-open", "start", "end", "length", "sub-files", "audio-files", "external-files",
            "vo", "wid", "d3d11-output-mode", "d3d11-exclusive-fs", "d3d11-composition-size", "force-window",
            "input-default-bindings", "input-vo-keyboard", "input-media-keys"
        ];
        return owned.Any(option => key.Equals(option, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(option + "-", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        foreach (var copy in _copies)
        {
            try { File.Delete(copy); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _copies.Clear();
    }
}
