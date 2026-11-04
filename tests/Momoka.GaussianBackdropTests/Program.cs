namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        GaussianBackdropTests.Register();
        var result = TestHarness.Run();
        var pixels = new byte[1280 * 720 * 4];
        new Random(1).NextBytes(pixels);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Momoka.Emby.GaussianBackdrop.Apply(pixels, 1280, 720, Momoka.Emby.GaussianBackdrop.Sigma);
        Console.WriteLine($"1280×720 sigma12 单图处理：{watch.ElapsedMilliseconds} ms（Release CPU读数，非时间断言）。");
        return result;
    }
}
