using Momoka.Danmaku;

namespace Momoka.Shell;

/// <summary>只判断原生热力层的实际读数；不产生柱子、不替代正式页面或弹幕加载。</summary>
internal readonly record struct DanmakuHeatBarReading(double Left, double Top, double Width, double Height, bool SharedBrush);

internal static class DanmakuHeatProbeEvidence
{
    internal static bool Matches(DanmakuHeat heat, double width, double height, IReadOnlyList<DanmakuHeatBarReading> bars)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 1 || height <= 0 || heat.IsEmpty) return false;
        var levels = heat.Levels(Math.Clamp((int)Math.Round(width / 2), 120, 600));
        var slot = width / levels.Length;
        var ceiling = Math.Max(1, height * 0.6);
        var drawn = 0;
        for (var index = 0; index < levels.Length; index++)
        {
            if (levels[index] <= 0.001) continue;
            if (drawn >= bars.Count) return false;
            var bar = bars[drawn++];
            var barHeight = Math.Max(1, levels[index] * ceiling);
            if (!bar.SharedBrush || !Near(bar.Left, index * slot) || !Near(bar.Top, height - barHeight)
                || !Near(bar.Width, Math.Max(1, slot)) || !Near(bar.Height, barHeight)) return false;
        }
        return drawn > 0 && drawn == bars.Count;
    }

    /// <summary>同尺寸稳定实屏中，轨道区域至少要有可见颜色变化；坏尺寸或越界直接拒绝。</summary>
    internal static int ChangedPixels(byte[] before, byte[] after, int width, int height, int left, int top, int right, int bottom)
    {
        if (width <= 0 || height <= 0 || before.Length != (long)width * height * 4 || after.Length != before.Length
            || left < 0 || top < 0 || right > width || bottom > height || right <= left || bottom <= top)
            throw new ArgumentException("热力实屏尺寸或轨道区域无效");
        var count = 0;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var index = (y * width + x) * 4;
                if (Math.Abs(before[index] - after[index]) + Math.Abs(before[index + 1] - after[index + 1])
                    + Math.Abs(before[index + 2] - after[index + 2]) >= 12) count++;
            }
        return count;
    }

    private static bool Near(double actual, double expected) => double.IsFinite(actual) && Math.Abs(actual - expected) <= 0.1;
}
