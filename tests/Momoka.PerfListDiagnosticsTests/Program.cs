using System.Text;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static class PerfListDiagnosticsProgram
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("PERF-06／PERF-10 真实源码回归（假UI／内存来源，无窗口、真实库、设置或播放）");
        Console.WriteLine("生产源码根：" + LibraryViewModel.FixtureSourceRoot);
        Console.WriteLine("LibraryViewModel 当前源码 SHA256：" + LibraryViewModel.FixtureSourceHash);
        SourceLinkTests.Register();
        UpcomingPaginationTests.Register();
        DiagnosticsCoalescingTests.Register();
        DiagnosticsTests.Register();
        DiagnosticsTitleProbeTests.Register();
        PerformanceCompletionTests.Register();
        return TestHarness.Run();
    }
}
