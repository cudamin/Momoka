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
