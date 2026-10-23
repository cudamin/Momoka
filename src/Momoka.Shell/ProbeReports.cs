using System.Text;
using System.Text.Json;

namespace Momoka.Shell;

/// <summary>两种探针共用的真实收尾。仅窗口、应用退出和报告 IO 是调用方提供的边界。</summary>
internal static class ProbeFinalization
{
    internal static async Task RunAsync(Func<Task> run, Func<bool> passed,
        Action saveReport, Action<Exception> recordFailure, Action dispose,
        Action exit, Action<int> setExitCode, Action? saveScope = null)
    {
        var failed = true;
        setExitCode(1);
        try
        {
            await run();
            failed = !passed();
        }
        catch (Exception error)
        {
            Fail(error);
        }
        finally
        {
            try
            {
                // 范围也属于 Shell 报告。先写它，失败仍写正文，正文便能带上这次失败证据。
                if (saveScope is not null) Attempt(saveScope);
                Attempt(saveReport);
            }
            finally
            {
                try
                {
                    Attempt(dispose);
                }
                finally
                {
                    // 未完成、任一报告 IO 或关闭失败都保留非零；退出不受它们阻挡。
                    setExitCode(failed ? 1 : 0);
                    exit();
                }
            }
        }

        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { Fail(error); }
        }

        void Fail(Exception error)
        {
            failed = true;
            try { recordFailure(error); }
            catch (Exception)
            {
                // 记录失败本身也不能挡住退出；不可递归调用同一报告路径。
            }
        }
    }
}

/// <summary>字幕探针的内存证据与真实 Record/最终写路径，不持有任何 UI 或设置服务。</summary>
internal sealed class SubtitleProbeReport
{
    private readonly string _path;
    private readonly Action<string, string, Encoding> _writer;
    private readonly StringBuilder _report = new();

    internal SubtitleProbeReport(string logDirectory, Action<string, string, Encoding>? writer = null)
    {
        _path = Path.Combine(logDirectory, "subtitle-probe.txt");
        _writer = writer ?? File.WriteAllText;
    }

    internal string Text => _report.ToString();

    internal Task RunAsync(Func<Task> run, Action dispose, Action exit, Action<int> setExitCode) =>
        ProbeFinalization.RunAsync(run, () => true, Save,
            error => _report.AppendLine($"[失败] {error}"), dispose, exit, setExitCode);

    internal void Record(string name, bool ok, string detail)
    {
        _report.AppendLine($"[{(ok ? "通过" : "失败")}] {name} — {detail}");
        Save();
        if (!ok) throw new InvalidOperationException(name);
    }

    private void Save() => _writer(_path, _report.ToString(), Encoding.UTF8);
}

/// <summary>Shell 探针的实际 Case/范围/报告路径；WinUI 走查作为 RunAsync 的工作委托传入。</summary>
internal sealed class ShellProbeReport
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly string _logDirectory;
    private readonly Action<string, string, Encoding> _writer;
    private readonly List<Check> _checks = [];
    private readonly List<Skipped> _skipped = [];
    private readonly HashSet<string>? _sections;
    private readonly IReadOnlyList<string> _allSections;

    internal ShellProbeReport(string logDirectory, IReadOnlyCollection<string>? sections,
        IReadOnlyList<string> allSections, Action<string, string, Encoding>? writer = null)
    {
        _logDirectory = logDirectory;
        _sections = sections is null ? null : new(sections, StringComparer.Ordinal);
        _allSections = allSections;
        _writer = writer ?? File.WriteAllText;
    }

    internal IReadOnlyList<Check> Checks => _checks;
    internal sealed record Check(string Name, bool Passed, string Detail);
    private sealed record Skipped(string Section, string Name);

    internal Task RunAsync(Func<Task> run, bool inspect, Action dispose, Action exit, Action<int> setExitCode) =>
        ProbeFinalization.RunAsync(run, () => inspect || _checks.Count > 0 && _checks.All(check => check.Passed),
            Save, error => _checks.Add(new Check("探针宿主", false, error.ToString())),
            dispose, exit, setExitCode, SaveScope);

    internal async Task CaseAsync(string name, Func<Task> run, int timeoutSeconds, string? section)
    {
        if (section is null)
        {
            _checks.Add(new Check("探针分区", false, $"用例「{name}」没有分区；请在 ShellNavigationProbe.Sections.cs 登记或显式传入 section"));
            Save();
            return;
        }
        if (_sections is not null && !_sections.Contains(section))
        {
            _skipped.Add(new Skipped(section, name));
            SaveScope();
            return;
        }
        try
        {
            await run().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            _checks.Add(new Check(name, true, ""));
        }
        catch (Exception error) { _checks.Add(new Check(name, false, error.ToString())); }
        Save();
    }

    private void Save() => _writer(Path.Combine(_logDirectory, "shell-probe.json"),
        JsonSerializer.Serialize(_checks, new JsonSerializerOptions { WriteIndented = true }), Utf8);

    private void SaveScope()
    {
        var selected = _sections is null ? "全量" : string.Join(", ", _sections.OrderBy(section => section, StringComparer.Ordinal));
        var unselected = _sections is null
            ? "无"
            : string.Join(", ", _allSections.Where(section => !_sections.Contains(section)));
        if (unselected.Length == 0) unselected = "无";
        var lines = new List<string>
        {
            $"执行区域：{selected}",
            $"未执行区域：{unselected}",
            $"未执行检查：{_skipped.Count}",
        };
        foreach (var group in _skipped.GroupBy(item => item.Section, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            lines.Add($"[{group.Key}]");
            lines.AddRange(group.Select(item => "- " + item.Name));
        }
        _writer(Path.Combine(_logDirectory, "shell-probe-scope.txt"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine, Utf8);
    }
}
