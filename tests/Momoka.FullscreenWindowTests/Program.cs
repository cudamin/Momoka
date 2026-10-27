namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("本项目不接受运行参数；生产源码通过 FullscreenSource / SelfCheckSource 构建参数选择。");
            return 2;
        }
        Console.WriteLine("全屏标题栏生命周期离线测试（无 HWND / WinUI / 媒体 / 自检执行）");
        foreach (var source in FullscreenSourceEvidence.Sources)
        {
            Console.WriteLine($"SOURCE {source.Name}: {source.Path}");
            Console.WriteLine($"SHA256 {source.Sha256}; unchanged slices={source.Slices.Length}");
        }
        SourceEvidenceTests.Register();
        FullscreenLifecycleTests.Register();
        FullscreenRegionTests.Register();
        FullscreenContractTests.Register();
        return TestHarness.Run();
    }
}
