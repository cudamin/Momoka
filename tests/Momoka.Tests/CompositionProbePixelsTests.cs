using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CompositionProbePixelsTests
{
    public static void Register()
    {
        Test("合成像素：BGRA转换保持RGB和COLORREF顺序", () =>
        {
            byte[] bytes = [0x33, 0x22, 0x11, 0x00, 0x66, 0x55, 0x44, 0xFF];
            Assert.Equal(0x332211u, CompositionProbePixels.ColorRef(bytes, 2, 1, 0, 0));
            Assert.Equal("#112233", CompositionProbePixels.Rgb(bytes, 2, 1, 0, 0));
            Assert.Equal("#445566", CompositionProbePixels.Rgb(bytes, 2, 1, 1, 0));
        });
        Test("合成像素：顶部向下布局和末行末列正确", () =>
        {
            byte[] bytes = [0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0];
            Assert.Equal("#FF0000", CompositionProbePixels.Rgb(bytes, 2, 2, 0, 0));
            Assert.Equal("#00FF00", CompositionProbePixels.Rgb(bytes, 2, 2, 1, 0));
            Assert.Equal("#0000FF", CompositionProbePixels.Rgb(bytes, 2, 2, 0, 1));
            Assert.Equal("#FFFFFF", CompositionProbePixels.Rgb(bytes, 2, 2, 1, 1));
        });
        Test("合成像素：完整数据尺寸必须准确，不接受截断或多余像素", () =>
        {
            Assert.Throws<ArgumentException>(() => CompositionProbePixels.ColorRef(new byte[3], 1, 1, 0, 0));
            Assert.Throws<ArgumentException>(() => CompositionProbePixels.ColorRef(new byte[8], 1, 1, 0, 0));
            Assert.Throws<ArgumentException>(() => CompositionProbePixels.ColorRef([], 0, 1, 0, 0));
        });
        Test("合成像素：采样点越界和尺寸溢出明确失败", () =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CompositionProbePixels.ColorRef(new byte[4], 1, 1, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CompositionProbePixels.ColorRef(new byte[4], 1, 1, 0, 1));
            Assert.Throws<OverflowException>(() => CompositionProbePixels.ColorRef([], int.MaxValue, 2, 0, 0));
        });
        Test("合成像素：真实采样只捕一次且保防遮挡和原超时", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
            Assert.NotNull(root);
            var source = File.ReadAllText(Path.Combine(root!.FullName, "src/Momoka.Shell/CompositionPlaybackProbe.cs"));
            var start = source.IndexOf("    private static string SampleFrame(", StringComparison.Ordinal);
            var end = source.IndexOf("    /// <summary>", start + 10, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            var method = source[start..end];
            Assert.Equal(1, method.Split("CapturePixels(window)", StringSplitOptions.None).Length - 1);
            Assert.False(method.Contains("Native.GetPixel", StringComparison.Ordinal));
            Assert.Contains("frame.RgbAt(spot.X, spot.Y)", method);
            Assert.Contains("CancellationTokenSource(TimeSpan.FromSeconds(120))", source);
            var pixels = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/CompositionPlaybackProbe.Pixels.cs"));
            // 变量名跟着采样方法一起改过（window.Handle → handle），判据本身没变：屏幕点必须还落在同一个窗口上。
            Assert.Contains("Native.GetAncestor(Native.WindowFromPoint(point), 2) != handle", pixels);
            Assert.Contains("像素探针窗口被遮挡或移到屏外", pixels);
        });
    }
}
