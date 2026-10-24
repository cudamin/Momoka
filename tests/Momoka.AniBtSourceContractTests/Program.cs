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
