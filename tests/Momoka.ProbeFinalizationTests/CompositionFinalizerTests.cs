using Momoka.Shell;

namespace Momoka.Tests
{
    internal static partial class Program
    {
        private static void RegisterCompositionFinalizer()
        {
            foreach (var failure in new[] { "none", "write", "stop", "dispose" })
                Test("R17-004-composition-finalizer-" + failure, async () =>
                {
                    ProbeApplication.Current.Reset();
                    var fixture = new CompositionFinalizerFixture();
                    fixture.Handle.FailStop = failure == "stop";
                    fixture.Report.FailWrite = failure == "write";
                    fixture.Report.FailDispose = failure == "dispose";
                    try { await fixture.RunAsync(); } catch (IOException) { }
                    Equal(failure == "none" ? 0 : 1, fixture.ExitCode, "failed report or cleanup cannot publish success");
                    Ensure(fixture.Page.Released && fixture.Window.Disposed && fixture.Handle.Disposed, "each owner cleans despite preceding failure");
                    Equal(1, ProbeApplication.Current.Exited, "exit is always reached");
                });
            foreach (var failure in new[] { "none", "write", "stop" })
                Test("R17-004-motion-finalizer-" + failure, async () =>
                {
                    var fixture = new MotionFinalizerFixture();
                    fixture.Handle.FailStop = failure == "stop";
                    fixture.Report.FailWrite = failure == "write";
                    try { await fixture.RunAsync(); } catch (IOException) { }
                    Equal(failure == "none" ? 0 : 1, fixture.ExitCode, "motion write/cleanup failure is nonzero");
                    Ensure(fixture.Handle.Disposed && fixture.Shell.Closed && fixture.Window.Disposed && fixture.Services.Closed,
                        "motion cleans every owner independently");
                });
            Test("R17-004-local-report-write-does-not-skip-cleanup", () =>
            {
                var blocked = Path.Combine(_output, "blocked-local-report");
                Directory.CreateDirectory(Path.Combine(blocked, "home-button-edges.txt"));
                var fixture = new ButtonFinalizerFixture(blocked);
                try { fixture.Run(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                Ensure(fixture.Removed && !fixture.TopMost, "local report failure cannot retain page or topmost state");
                return Task.CompletedTask;
            });
        }
    }

}

namespace Momoka.Shell
{
    internal sealed partial class ButtonFinalizerFixture(string directory)
    {
        private readonly ButtonOptions _options = new(directory);
        private readonly ButtonRoot _root = new();
        private readonly ButtonWindow _window = new();
        internal bool Removed => _root.Children.Removed;
        internal bool TopMost => _window.TopMost;
        private sealed record ButtonOptions(ButtonPaths Paths) { internal ButtonOptions(string directory) : this(new ButtonPaths(directory)) { } }
        private sealed record ButtonPaths(string LogDirectory);
        private sealed class ButtonRoot { internal ButtonChildren Children { get; } = new(); }
        private sealed class ButtonChildren { internal bool Removed; internal void Remove(object holder) => Removed = true; }
        private sealed class ButtonWindow { internal bool TopMost { get; set; } = true; }
    }
    internal static class ThemeHost { internal static void Apply(object theme) { } }
    internal sealed partial class MotionFinalizerFixture
    {
        internal CompositionFinalizerFixture.FixtureHandle Handle { get; } = new();
        internal CompositionFinalizerFixture.FixtureReport Report { get; } = new();
        internal CompositionFinalizerFixture.FixtureWindow Window { get; } = new();
        internal FixtureShell Shell { get; } = new();
        internal FixtureServices Services { get; } = new();
        internal CancellationTokenSource Stopping { get; } = new();
        internal int ExitCode { get; private set; } = 1;
        internal sealed class FixtureShell { internal bool Closed; internal void Shutdown() => Closed = true; }
        internal sealed class FixtureServices { internal bool Closed; internal Task DisposeAsync() { Closed = true; return Task.CompletedTask; } }
    }
    internal sealed partial class CompositionFinalizerFixture
    {
        internal FixtureHandle Handle { get; } = new();
        internal FixturePage Page { get; } = new();
        internal FixtureWindow Window { get; } = new();
        internal FixtureReport Report { get; } = new();
        internal CancellationTokenSource Stopping { get; } = new();
        internal int ExitCode { get; private set; } = 1;
        internal sealed class FixtureHandle
        {
            internal bool FailStop; internal bool Disposed;
            internal Task StopAsync() => FailStop ? Task.FromException(new IOException("stop")) : Task.CompletedTask;
            internal Task DisposeAsync() { Disposed = true; return Task.CompletedTask; }
        }
        internal sealed class FixturePage { internal bool Released; internal void ReleaseVideoSurface() => Released = true; }
        internal sealed class FixtureWindow
        {
            internal bool Disposed;
            internal event Action? Closed { add { } remove { } }
            internal void Dispose() => Disposed = true;
        }
        internal sealed class FixtureReport
        {
            internal bool FailWrite; internal bool FailDispose;
            internal void Write(string message) { if (FailWrite) throw new IOException("report"); }
            internal void Dispose() { if (FailDispose) throw new IOException("dispose report"); }
        }
    }
}
