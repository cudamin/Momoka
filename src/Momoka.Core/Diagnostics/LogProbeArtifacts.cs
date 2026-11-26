using System.Globalization;

namespace Momoka.Diagnostics;

/// <summary>
/// 日志目录里探针一次运行产物的保留期。
/// <para>
/// <see cref="FileLogSink"/> 只拥有自己的 <c>app-*.log</c>（单卷 8 MiB、总量 64 MiB、14 天）。但每个
/// <c>--probe-*</c> 进程还会在 <c>%LOCALAPPDATA%\Momoka\logs</c> 下新建一个
/// <c>&lt;探针&gt;-yyyyMMdd-HHmmssfff-&lt;pid&gt;</c> 运行目录，里面是这次走查的截图、WebView2 配置和测量
/// 文件，从来没有回收：实测两个月 2547 次 shell 探针留下 36.7 GB / 62.7 万个文件。这里补上那部分预算。
/// </para>
/// <para>
/// 口径（2026-11-19 用户确认）：运行目录名里带着 UTC 启动时间，超过 <see cref="Retain"/> 的整棵删掉；
/// <b>每类探针永远保留最新一次</b>，即使它更旧 —— 否则一段时间没跑，最后一份证据也会被回收。
/// <c>trailer-preview</c>／<c>trailer-web-fixture</c> 是按预告分的容器，只回收其中过期的子目录，容器本身留下。
/// 不认识的条目（用户手工保留的 Gallery 源码、sidebar 证据、真正的日志文件）一律不碰。
/// </para>
/// <para>
/// 删除失败（被占用、权限不足）不抛给调用方：这是后台清理，下一次启动或探针启动会再试。目录枚举和删除
/// 都只发生在启动路径上，正常写入不扫描目录。
/// </para>
/// </summary>
public static class LogProbeArtifacts
{
    /// <summary>默认保留期；用户口径是 3 天。</summary>
    public static readonly TimeSpan Retain = TimeSpan.FromDays(3);

    /// <summary>这些前缀后面跟 <c>yyyyMMdd-HHmmssfff-pid</c>，是一次探针运行；由 Shell 的 <c>Program</c> 创建。</summary>
    private static readonly string[] RunPrefixes =
    [
        "shell-probe", "subtitle-probe", "composition-probe", "cursor-probe",
        "player-motion-probe", "standalone-playback-probe", "navigation-timing", "trailer-web-fixture"
    ];

    /// <summary>这些是容器目录，子目录一次预告一个，按最后写入时间回收。</summary>
    private static readonly string[] ContainerDirectories = ["trailer-preview", "trailer-web-fixture"];

    private const string StampFormat = "yyyyMMdd-HHmmssfff";

    /// <summary>运行目录名里时间戳的长度：<c>yyyyMMdd-HHmmssfff</c>。</summary>
    private const int StampLength = 18;

    /// <summary>
    /// 回收 <paramref name="logDirectory"/> 下超过 <paramref name="retain"/> 的探针产物，返回删掉的目录数。
    /// <paramref name="maximumDeletes"/> 给启动路径一个上限：最老的先删，没删完的下次再继续。
    /// </summary>
    public static int Prune(string logDirectory, DateTimeOffset now, TimeSpan retain, int maximumDeletes = int.MaxValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        if (retain < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retain));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDeletes);

        // 与 FileLogSink 相同，目录及祖先都必须是普通目录；容器也不能把回收引到树外。
        if (!IsOrdinaryDirectoryPath(logDirectory)) return 0;

        var cutoff = now - retain;
        var candidates = new List<Candidate>();
        var runs = new List<(string Path, string Prefix, DateTime Started)>();

        foreach (var path in Directories(logDirectory))
        {
            if (!TryReadRun(Path.GetFileName(path), out var prefix, out var started)) continue;
            runs.Add((path, prefix, started.UtcDateTime));
        }

        // 每类探针保留最新一次 —— 即使它已经过了保留期。
        foreach (var group in runs.GroupBy(run => run.Prefix, StringComparer.Ordinal))
        {
            var newest = group.MaxBy(run => run.Started).Path;
            foreach (var run in group)
                if (run.Path != newest && run.Started < cutoff.UtcDateTime)
                    candidates.Add(new Candidate(run.Path, run.Started));
        }

        foreach (var name in ContainerDirectories)
        {
            var container = Path.Combine(logDirectory, name);
            var children = Directories(container)
                .Select(path => new Candidate(path, LastWriteUtc(path)))
                .Where(candidate => candidate.SortKey != DateTime.MinValue)
                .ToArray();
            if (children.Length == 0) continue;
            var newest = children.MaxBy(candidate => candidate.SortKey)!;
            candidates.AddRange(children.Where(candidate => candidate.Path != newest.Path
                && candidate.SortKey < cutoff.UtcDateTime));
        }

        // 上限按最老的先删，保证积压时每次启动都在推进；删不动的目录下次还会被枚举到。
        var deleted = 0;
        var attempted = 0;
        foreach (var candidate in candidates.OrderBy(candidate => candidate.SortKey))
        {
            if (attempted >= maximumDeletes) break;
            attempted++;
            if (TryDelete(candidate.Path)) deleted++;
        }
        return deleted;
    }

    private static string[] Directories(string path)
    {
        try
        {
            if (!IsOrdinaryDirectoryPath(path)) return [];
            return Directory.GetDirectories(path).Where(IsOrdinaryDirectoryPath).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static bool IsOrdinaryDirectoryPath(string path)
    {
        try
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
                if ((File.GetAttributes(directory.FullName) & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                    != FileAttributes.Directory) return false;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static DateTime LastWriteUtc(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch (IOException) { return DateTime.MinValue; }
        catch (UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    /// <summary>
    /// 认出 <c>&lt;探针&gt;-yyyyMMdd-HHmmssfff-&lt;pid&gt;</c>。名字里的时间是 UTC —— Program 创建目录时
    /// 用的就是 <see cref="DateTime.UtcNow"/>，不能按本地时间解释，否则时区一换保留期就偏了。
    /// </summary>
    private static bool TryReadRun(string name, out string prefix, out DateTimeOffset started)
    {
        foreach (var candidate in RunPrefixes)
        {
            if (!name.StartsWith(candidate + "-", StringComparison.Ordinal)) continue;
            var rest = name.AsSpan(candidate.Length + 1);
            if (rest.Length < StampLength + 2 || rest[StampLength] != '-') continue;
            if (!DateTime.TryParseExact(rest[..StampLength], StampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var stamp)) continue;
            var pid = rest[(StampLength + 1)..];
            if (pid.IsEmpty || !pid.ContainsOnlyDigits()) continue;
            prefix = candidate;
            started = new DateTimeOffset(stamp, TimeSpan.Zero);
            return true;
        }

        prefix = "";
        started = default;
        return false;
    }

    private static bool ContainsOnlyDigits(this ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (character is < '0' or > '9') return false;
        return true;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            // 枚举后容器或祖先可能被替换，删除前重新检查整条路径。
            if (!IsOrdinaryDirectoryPath(path)) return false;
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private readonly record struct Candidate(string Path, DateTime SortKey);
}
