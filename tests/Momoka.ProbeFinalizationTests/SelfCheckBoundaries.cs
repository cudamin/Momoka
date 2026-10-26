using System.Text;
using Momoka.Infrastructure;

namespace Momoka.Shell;

// Only precomputed UI readings and UI/IO endpoints are replaced. These do not decide the result.
internal sealed class SelfCheckInput(AppPaths paths)
{
    internal AppPaths Paths { get; } = paths;
    internal bool DumpUi { get; init; }
    internal string Readings { get; init; } = "[通过] A — isolated fixture\n";
    internal int Failures { get; init; }
    internal Action? BeforeReadings { get; init; }
    internal Func<Task>? AfterReport { get; init; }
}

internal sealed class ProbeWindow
{
    internal int Closed { get; private set; }
    internal Action? OnClose { get; init; }
    internal void Close()
    {
        Closed++;
        OnClose?.Invoke();
    }
}

internal sealed class ProbeShell(SelfCheckInput input)
{
    internal bool SignInVisible => true; // A completed offline sign-in view has no server round trips.
    internal ProbePlayer PlayerRoot { get; } = new(input);
}

internal sealed class ProbePlayer(SelfCheckInput input)
{
    internal Task CaptureTransportSamplesAsync(string directory) => input.AfterReport?.Invoke() ?? Task.CompletedTask;
}

internal static partial class ShellSelfCheck
{
    // Thin accessors only; the result storage, write and close/finally logic are source-linked production.
    internal static void ResetForFixture() => ExitCode = 1;
    internal static bool CompleteForFixture(AppPaths paths, string content, int failures,
        Action<string, string, Encoding>? writer = null) => CompleteReport(paths, content, failures, writer);
    internal static bool WriteForFixture(AppPaths paths, string name, string content,
        Action<string, string, Encoding>? writer = null) => Write(paths, name, content, writer);
    internal static Task FinishForFixture(Func<Task> run, Action close) => FinishAsync(run, close);

    // These unexecuted UI readers only satisfy the verbatim callback's framework-free compile contract.
    private static object? _seasons;
    private static object? _commands;
    private static Task<object?> ProbeSeasonsAsync(ProbeShell shell, IServiceProvider services) =>
        throw new InvalidOperationException("Fixture must not probe a server");
    private static Task<object?> ProbeCommandsAsync(IServiceProvider services) =>
        throw new InvalidOperationException("Fixture must not probe a server");
    private static Task ShootAsync(ProbeShell shell, SelfCheckInput options) => Task.CompletedTask;
    private static void Dump(ProbeShell shell, StringBuilder tree, int depth) => tree.AppendLine("UI boundary");
}
