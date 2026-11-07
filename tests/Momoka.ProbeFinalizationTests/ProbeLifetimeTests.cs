using Momoka.Shell;

namespace Momoka.Tests
{
    internal static partial class Program
    {
        private static void RegisterProbeLifetimes()
        {
            Test("R17-012-transport-closes-page-and-vm", async () =>
            {
                var fixture = new ProbeLifetimeFixture();
                await fixture.CloseTransportAsync();
                Ensure(fixture.Page.Detached && fixture.Page.Stopped, "removed playback page releases subscription and timer ownership");
                Ensure(fixture.Vm.Closed, "resident VM async work was drained");
            });
            Test("R17-012-render-timeout-removes-static-subscription", async () =>
            {
                CompositionTarget.Reset();
                var fixture = new ProbeLifetimeFixture();
                try { await fixture.WaitForRenderAsync(); }
                catch (TimeoutException) { }
                Equal(0, CompositionTarget.Count, "timeout must remove static rendering callback");
            });
            Test("R17-012-render-success-removes-static-subscription", async () =>
            {
                CompositionTarget.Reset();
                var fixture = new ProbeLifetimeFixture();
                var waiting = fixture.WaitForRenderAsync();
                CompositionTarget.Emit();
                await waiting;
                Equal(0, CompositionTarget.Count, "success must remove rendering callback");
            });
        }
    }
}

namespace Momoka.Shell
{
    internal sealed partial class ProbeLifetimeFixture
    {
        private readonly FixtureWindow _window = new();
        private readonly FixtureRoot _root = new();
        internal FixturePage Page { get; } = new();
        internal FixtureVm Vm { get; } = new();
        internal FixtureRun Run { get; } = new();
        private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal sealed class FixtureWindow { internal bool Fullscreen { get; set; } }
        internal sealed class FixtureRoot { internal List<FixturePage> Children { get; } = []; }
        internal sealed class FixturePage
        {
            internal bool Detached;
            internal bool Stopped;
            internal void Detach() => Detached = true;
            internal void Shutdown() => Stopped = true;
        }
        internal sealed class FixtureVm { internal bool Closed; internal Task ShutdownAsync() { Closed = true; return Task.CompletedTask; } }
        internal sealed class FixtureRun { internal void Mark(string stage) { } }
    }
    internal static class CompositionTarget
    {
        internal static event EventHandler<object>? Rendering;
        internal static int Count => Rendering?.GetInvocationList().Length ?? 0;
        internal static void Reset() => Rendering = null;
        internal static void Emit() => Rendering?.Invoke(null, new());
    }
}
