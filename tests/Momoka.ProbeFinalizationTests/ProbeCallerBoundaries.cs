using System.Text;
using Momoka.Infrastructure;

namespace Momoka.Shell;

// The production probe entry methods are compiled unchanged. Only their UI walk and UI endpoints below
// are absent: these substitutes have no WinRT/native methods, settings service, login or network client.
internal sealed record StartupOptions(AppPaths Paths)
{
    internal bool InspectShell { get; init; }
    internal bool InspectSubtitles { get; init; }
    internal IReadOnlySet<string>? ProbeSections { get; init; }
    internal ProbeHostWindow Window { get; init; } = new();
    internal Func<Action<string, bool, string>, Task> SubtitleBody { get; init; } = _ => Task.CompletedTask;
    internal Func<Func<string, bool, string?, Task>, Task> ShellBody { get; init; } = _ => Task.CompletedTask;
}

internal sealed class ProbeHostWindow : IDisposable
{
    internal int Disposed { get; private set; }
    public void Dispose() => Disposed++;
}

internal sealed class ProbeApplication
{
    internal static ProbeApplication Current { get; } = new();
    internal int Exited { get; private set; }
    internal void Reset() => Exited = 0;
    internal void Exit() => Exited++;
}

internal sealed class ProbeGrid;

internal sealed class ProbeClientResource(HttpMessageHandler handler) : IDisposable
{
    internal static int Disposed { get; private set; }
    internal static void Reset() => Disposed = 0;
    public void Dispose()
    {
        Disposed++;
        handler.Dispose();
    }
}

// Value-only layout and registration seams: no WinUI, window, event loop or user settings.
internal enum Visibility { Visible, Collapsed }
internal sealed class InspectLayout
{
    internal Visibility Visibility { get; set; } = Visibility.Visible;
}

internal sealed partial class InspectCheckFixture
{
    private readonly ShellProbeReport _report;
    internal InspectLayout Layout { get; } = new();
    internal Func<Task> Check { get; }
    internal Func<Task>? Action { get; set; }
    internal IReadOnlyList<ShellProbeReport.Check> Checks => _report.Checks;

    internal InspectCheckFixture(Func<Task> check, Action<string, string, Encoding> writer, AppPaths paths)
    {
        Check = check;
        paths.EnsureCreated();
        _report = new ShellProbeReport(paths.LogDirectory, null, ShellProbeSections.All, writer);
    }

    private Task CaseAsync(string name, Func<Task> run, int timeoutSeconds, string? section)
    {
        if (timeoutSeconds != 180 || section != ShellProbeSections.Library)
            throw new InvalidOperationException("Unexpected production case boundary");
        // Only shorten the timeout boundary; execution, timeout recording and writer are real production.
        return _report.CaseAsync(name, run, 1, section);
    }
}
