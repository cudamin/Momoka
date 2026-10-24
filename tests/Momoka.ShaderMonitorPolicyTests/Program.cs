using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static ShellRuntime _shell = null!;
    private static int Main(string[] args)
    {
        if (args is not [var shell, var root]) throw new ArgumentException("Provide this workspace current x64 Release Shell and root");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        _shell = new ShellRuntime(shell, root);
        PlayerConstructorBoundary.Register(_shell.VmType, _shell.DispatcherType);
        Register();
        return Run();
    }
    private static void Case(string name, Func<Task> test) => Test("R052 " + name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(test);
    });
    private static ShaderSurface Small(int monitorWidth = 2560, int monitorHeight = 1440) => new(2048, 1152, monitorWidth, monitorHeight, false);
    private static ShaderSurface Full(int monitorWidth = 2560, int monitorHeight = 1440) => new(monitorWidth, monitorHeight, monitorWidth, monitorHeight, true);
    private static void PlanApplied(MonitorFixture f, ShaderSurface surface)
    {
        Assert.True(ReferenceEquals(f.Expected(surface).Group, f.Plan.Group), "prepared monitor plan must match the playback snapshot");
        Assert.True(ReferenceEquals(f.Plan.Group, f.Active), "adopted group must agree with restoring automatic mode");
    }
    private static void Register()
    {
        foreach (var full in new[] { false, true })
            foreach (var monitor in new[] { (2880, 1800), (2800, 1575), (3840, 2160) })
                Case((full ? "full" : "same-active-window") + " cross monitor " + monitor, async () =>
                {
                    using var f = new MonitorFixture(_shell, full ? Full() : Small());
                    var next = full ? Full(monitor.Item1, monitor.Item2) : Small(monitor.Item1, monitor.Item2);
                    f.Move(next);
                    await f.Settle();
                    PlanApplied(f, next);
                    Assert.Equal(next.Active, f.Watch.Output, "the actual output size remains physical active pixels");
                    var group = f.Active;
                    await f.Restore();
                    Assert.True(ReferenceEquals(group, f.Active), "automatic move and explicit restore choose the same group");
                });
        Case("same monitor ordinary resize never changes fixed plan", async () =>
        {
            using var f = new MonitorFixture(_shell, Small(2880, 1800));
            var initial = f.Active;
            f.Move(new ShaderSurface(640, 360, 2880, 1800, false));
            f.Tick();
            await f.Settle();
            Assert.True(ReferenceEquals(initial, f.Active));
            Assert.Equal(0, f.Handle.Writes.Count, "fixed full-monitor policy must not cold-compile on window size");
            Assert.Equal(f.Expected(f.Surface).Measure.Tier, f.Watch.Tier, "watch tier must use the same target as the plan");
            Assert.Equal((640, 360), f.Watch.Output);
        });
        Case("same monitor fullscreen enter and leave does not rewrite shader", async () =>
        {
            using var f = new MonitorFixture(_shell, Small(2880, 1800));
            f.Move(Full(2880, 1800));
            await f.Settle();
            f.Move(Small(2880, 1800));
            await f.Settle();
            Assert.Equal(0, f.Handle.Writes.Count);
            Assert.Equal(UpscaleTier.Sweet, f.Watch.Tier);
            PlanApplied(f, f.Surface);
        });
        Case("same-tier monitor move records size with zero shader writes", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            f.Move(Small(2640, 1485));
            await f.Settle();
            PlanApplied(f, f.Surface);
            Assert.Equal(0, f.Handle.Writes.Count);
        });
        Case("successful monitor adoption is not rewritten by later resize or fullscreen", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            f.Move(Small(2880, 1800));
            await f.Settle();
            PlanApplied(f, f.Surface);
            var writes = f.Handle.Writes.Count;
            Assert.True(writes > 0);
            f.Move(new ShaderSurface(800, 450, 2880, 1800, false));
            f.Tick();
            await f.Settle();
            f.Move(Full(2880, 1800));
            await f.Settle();
            f.Move(Small(2880, 1800));
            await f.Settle();
            Assert.Equal(writes, f.Handle.Writes.Count, "same prepared group does not issue another automatic transaction");
            PlanApplied(f, f.Surface);
        });
        Case("manual pin survives move and restoring auto adopts new monitor", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            var pinned = ShaderGroupCatalog.For(GpuTier.Low).First(g => g.Animated && g.Tier == UpscaleTier.Large);
            await f.Pin(pinned);
            var writes = f.Handle.Writes.Count;
            f.Move(Small(2880, 1800));
            await f.Settle();
            Assert.True(ReferenceEquals(pinned, f.Active));
            Assert.Equal(writes, f.Handle.Writes.Count, "monitor updates cannot replace a user pin");
            await f.Restore();
            PlanApplied(f, f.Surface);
        });
        Case("manually pinned off survives move then restores new automatic plan", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            await f.Pin(null);
            var writes = f.Handle.Writes.Count;
            f.Move(Small(2880, 1800));
            await f.Settle();
            Assert.Null(f.Active);
            Assert.Equal(writes, f.Handle.Writes.Count);
            await f.Restore();
            PlanApplied(f, f.Surface);
        });
        Case("settings modified during play are not adopted by monitor move", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            f.Settings.Settings.Shaders.Enabled = false;
            f.Settings.Settings.Shaders.Gpu = GpuTier.High;
            f.Move(Small(2880, 1800));
            await f.Settle();
            PlanApplied(f, f.Surface);
            Assert.NotNull(f.Active);
        });
        Case("disabled automation remains off across monitor changes", async () =>
        {
            using var f = new MonitorFixture(_shell, Small(), enabled: false);
            f.Move(Small(3840, 2160));
            await f.Settle();
            Assert.Null(f.Active);
            Assert.Equal(0, f.Handle.Writes.Count);
        });
        Case("ultra-high-resolution override stays off", async () =>
        {
            using var f = new MonitorFixture(_shell, Small(), width: 7680, height: 4320);
            f.Move(Small(3840, 2160));
            await f.Settle();
            Assert.Null(f.Active);
            Assert.Equal(0, f.Handle.Writes.Count);
        });
        Case("zero source dimensions use existing conservative plan without guessed size", async () =>
        {
            using var f = new MonitorFixture(_shell, Small(), width: 0, height: 0);
            f.Move(Small(2880, 1800));
            await f.Settle();
            PlanApplied(f, f.Surface);
            Assert.Equal(0d, f.Plan.Measure.Factor);
        });
        Case("move within the same monitor target has no double transaction", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            f.Move(Small(2880, 1800));
            await f.Settle();
            var writes = f.Handle.Writes.Count;
            f.Move(Small(2880, 1800));
            await f.Settle();
            Assert.Equal(writes, f.Handle.Writes.Count);
        });
        Case("back to first monitor restores its actual plan", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            f.Move(Small(2880, 1800));
            await f.Settle();
            f.Move(Small());
            await f.Settle();
            PlanApplied(f, f.Surface);
        });
        Case("launch ticket uses the same full monitor as the prepared plan", () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            Assert.Equal(f.Surface.Monitor, f.LaunchOutput);
            return Task.CompletedTask;
        });
        Case("adopt same monitor launch does not mistake a small window for monitor move", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            await f.Adopt();
            Assert.Equal(0, f.Handle.Transactions);
            Assert.Equal(UpscaleTier.Slight, f.Watch.Tier);
        });
        Case("successful alternate source adopted into same target", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            await f.Adopt(sourceChanged: true);
            PlanApplied(f, f.Surface);
            Assert.Equal(0, f.Handle.Transactions);
            Assert.Equal(f.Plan.Measure.Tier, f.Watch.Tier);
        });
        Case("pending monitor transaction does not queue same plan on fullscreen", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            f.Handle.HoldWrite();
            f.Move(Small(2880, 1800));
            await f.Handle.WriteStarted.Task;
            f.Move(Full(2880, 1800));
            f.Move(Small(2880, 1800));
            f.Handle.ReleaseWrite();
            await f.Settle();
            PlanApplied(f, f.Surface);
            Assert.Equal(1, f.Handle.Transactions, "same pending prepared target must not cold-compile repeatedly");
        });
        Case("failed automatic transaction stays visible and explicit restore retries", async () =>
        {
            using var f = new MonitorFixture(_shell, Small());
            var original = f.Active;
            f.Handle.Reject = true;
            f.Move(Small(2880, 1800));
            await f.Settle();
            Assert.True(ReferenceEquals(original, f.Active));
            f.Handle.Reject = false;
            await f.Restore();
            PlanApplied(f, f.Surface);
        });
        Case("fixed watcher accepts same active size and fresh monitor boundary", () =>
        {
            var watch = new OutputWatch(1920, 1080, Small(), UpscaleTier.Slight, fullMonitorTarget: true);
            var verdict = watch.Observe(Small(2880, 1800), DateTimeOffset.UnixEpoch);
            Assert.Equal(OutputChange.Rebuild, verdict.Change);
            Assert.Equal(UpscaleTier.Sweet, watch.Tier);
            Assert.Equal((2048, 1152), watch.Output);
            Assert.True(verdict.Discrete);
            return Task.CompletedTask;
        });
        Case("fixed watcher ordinary active resize changes output but not tier", () =>
        {
            var watch = new OutputWatch(1920, 1080, Small(2880, 1800), UpscaleTier.Sweet, fullMonitorTarget: true);
            var start = DateTimeOffset.UnixEpoch;
            Assert.Equal(OutputChange.Waiting, watch.Observe(new ShaderSurface(640, 360, 2880, 1800, false), start).Change);
            Assert.Equal(OutputChange.SizeOnly, watch.Tick(start + TimeSpan.FromMilliseconds(401)).Change);
            Assert.Equal((640, 360), watch.Output);
            Assert.Equal(UpscaleTier.Sweet, watch.Tier);
            return Task.CompletedTask;
        });
        Case("fixed watcher fullscreen same target does not rebuild", () =>
        {
            var watch = new OutputWatch(1920, 1080, Small(2880, 1800), UpscaleTier.Sweet, fullMonitorTarget: true);
            var verdict = watch.Observe(Full(2880, 1800), DateTimeOffset.UnixEpoch);
            Assert.Equal(OutputChange.SizeOnly, verdict.Change);
            Assert.Equal((2880, 1800), watch.Output);
            Assert.Equal(UpscaleTier.Sweet, watch.Tier);
            return Task.CompletedTask;
        });
        Case("fixed watcher invalid source uses the existing unknown factor tier", () =>
        {
            var watch = new OutputWatch(0, 0, Small(), UpscaleTier.Slight, fullMonitorTarget: true);
            Assert.Equal(OutputChange.SizeOnly, watch.Observe(Small(2880, 1800), DateTimeOffset.UnixEpoch).Change);
            Assert.Equal(UpscaleTier.Slight, watch.Tier);
            return Task.CompletedTask;
        });
        Case("legacy active-size watcher retains valid hysteresis", () =>
        {
            var watch = new OutputWatch(1920, 1080, Full(), UpscaleTier.Slight);
            var verdict = watch.Observe(Full(2880, 1800), DateTimeOffset.UnixEpoch);
            Assert.Equal(OutputChange.SizeOnly, verdict.Change);
            Assert.Equal(UpscaleTier.Slight, watch.Tier);
            return Task.CompletedTask;
        });
    }
}
