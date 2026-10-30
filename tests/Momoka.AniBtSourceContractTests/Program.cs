using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
#if !R042_LEGACY
        if (args is ["shell-probe"])
        {
            ShellProbeContractTests.Register();
            return Run();
        }
#endif
        Test("PERF 集成：AniBT 设置IO端点拒绝全部延迟保存API，不借新队列触碰文件", () =>
        {
            var store = new Momoka.Configuration.SettingsStore(new Momoka.Infrastructure.AppPaths(System.AppContext.BaseDirectory), null!);
            Assert.Throws<InvalidOperationException>(() => store.RegisterDeferredChange());
            Assert.Throws<InvalidOperationException>(() => _ = store.PendingVersion);
            Assert.Throws<InvalidOperationException>(() => store.CapturePendingSave(new Momoka.Configuration.AppSettings()));
            Assert.Throws<InvalidOperationException>(() => store.TrySaveSnapshot(null!));
            Assert.Throws<InvalidOperationException>(() => store.ReportPendingSaveFailure(new InvalidOperationException("fixture")));
            Assert.Equal(0, store.SaveCalls);
            Assert.Equal(5, store.ForbiddenAccesses);
        });
        CoreSourceContractTests.Register();
#if !R042_LEGACY
        CallerSourceContractTests.Register();
        ShellProbeContractTests.Register();
#endif
        return Run();
    }

    internal static void AsyncCase(string name, Func<UiQueue, Task> run) => Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(() => run(queue));
    });
}
