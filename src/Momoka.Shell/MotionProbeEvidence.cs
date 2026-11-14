namespace Momoka.Shell;

/// <summary>原生动效诊断的实际屏幕标记读数；没有窗口、业务或平台动画替身。</summary>
internal sealed record MotionMarkerFrame(double AtMs, bool AfterDisable, bool Stable, int Left, int Top,
    int Right, int Bottom, int Pixels, double Green)
{
    internal bool Visible => Pixels > 0;
}

internal sealed record MotionCandidateDecision(string Outcome, string Reason, bool NativeIntermediate,
    bool DisableSettled, bool HistorySettled, bool FirstContentComparable);

internal readonly record struct MotionMarkerRegion(int Left, int Top, int Right, int Bottom);

internal sealed record MotionRouteFailure(string Route, string ExceptionType, string Message);

internal static class MotionProbeEvidence
{
    /// <summary>路由串行运行，失败仍保留并继续下一路；全部完成后汇总拒绝，不能吞成通过。</summary>
    internal static async Task RunRoutesAsync(IEnumerable<string> routes,
        Func<string, Task> sample)
    {
        var failures = new List<MotionRouteFailure>();
        foreach (var route in routes)
        {
            try { await sample(route); }
            catch (Exception error) { failures.Add(new(route, error.GetType().Name, error.Message)); }
        }
        if (failures.Count > 0) throw new InvalidOperationException("原生候选检查失败：" + string.Join("；",
            failures.Select(value => value.Route + "：" + value.ExceptionType + "：" + value.Message)));
    }

    internal static MotionMarkerFrame Read(byte[] bgra, int width, int height, bool stable, double atMs, bool afterDisable,
        MotionMarkerRegion? region = null)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.Length
            || !double.IsFinite(atMs) || atMs < 0)
            throw new ArgumentException("实际帧尺寸或时间无效");
        var area = region ?? new MotionMarkerRegion(0, 0, width - 1, height - 1);
        if (area.Left < 0 || area.Top < 0 || area.Right >= width || area.Bottom >= height
            || area.Right < area.Left || area.Bottom < area.Top)
            throw new ArgumentException("实际帧标记区域无效");
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        var pixels = 0;
        long green = 0;
        for (var y = area.Top; y <= area.Bottom; y++)
            for (var x = area.Left; x <= area.Right; x++)
            {
                var index = (y * width + x) * 4;
                var g = bgra[index + 1];
                if (g < 80 || g < bgra[index] + 60 || g < bgra[index + 2] + 60) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                pixels++;
                green += g;
            }
        return new(atMs, afterDisable, stable, left, top, right, bottom, pixels, pixels == 0 ? 0 : (double)green / pixels);
    }

    internal static bool SamePresentation(MotionMarkerFrame sample, MotionMarkerFrame settled) =>
        sample.Stable && settled.Stable && sample.Visible && settled.Visible
        && Math.Abs(sample.Left - settled.Left) <= 1 && Math.Abs(sample.Top - settled.Top) <= 1
        && Math.Abs(sample.Right - settled.Right) <= 1 && Math.Abs(sample.Bottom - settled.Bottom) <= 1
        && Math.Abs(sample.Green - settled.Green) <= 5
        && Math.Abs(sample.Pixels - settled.Pixels) <= Math.Max(8, settled.Pixels * 0.03);

    internal static MotionCandidateDecision Evaluate(IReadOnlyList<MotionMarkerFrame> baseline,
        IReadOnlyList<MotionMarkerFrame> native, IReadOnlyList<MotionMarkerFrame> disabled,
        IReadOnlyList<MotionMarkerFrame> history)
    {
        if (new[] { baseline, native, disabled, history }.Any(frames => frames.Count < 3
            || frames.Any(frame => !frame.Stable || !double.IsFinite(frame.AtMs) || frame.AtMs < 0)
            || frames.Zip(frames.Skip(1)).Any(pair => pair.First.AtMs > pair.Second.AtMs)
            || !frames[^1].Visible))
            return new("inconclusive", "missing-or-unstable-presented-frames", false, false, false, false);
        var baselineFinal = baseline[^1];
        if (!baseline.TakeLast(3).All(frame => SamePresentation(frame, baselineFinal)))
            return new("inconclusive", "baseline-not-settled", false, false, false, false);
        if (new[] { native, disabled, history }.Any(frames => !SamePresentation(frames[^1], baselineFinal)))
            return new("retained", "candidate-final-presentation-differs-from-control", false, false, false, false);
        var intermediate = native.Any(frame => frame.Visible && !SamePresentation(frame, native[^1]));
        var afterDisable = disabled.Where(frame => frame.AfterDisable).Take(3).ToArray();
        var disableSettled = afterDisable.Length == 3 && afterDisable.All(frame => SamePresentation(frame, baselineFinal));
        var historySettled = history.Take(3).All(frame => SamePresentation(frame, baselineFinal));
        var firstBaseline = baseline.First(frame => frame.Visible).AtMs;
        var firstNative = native.First(frame => frame.Visible).AtMs;
        var comparable = firstNative <= firstBaseline + 50;
        if (!intermediate)
            return new("inconclusive", "native-intermediate-not-observed", false, disableSettled, historySettled, comparable);
        if (!disabled.Any(frame => !frame.AfterDisable && frame.Visible && !SamePresentation(frame, baselineFinal)))
            return new("inconclusive", "disable-before-native-start-not-proved", true, disableSettled, historySettled, comparable);
        if (!disableSettled)
            return new("retained", "in-flight-transition-did-not-settle-on-next-presented-frames", true, false, historySettled, comparable);
        if (!historySettled)
            return new("retained", "history-replayed-or-delayed-after-animation-disabled", true, true, false, comparable);
        if (!comparable)
            return new("retained", "first-marker-content-delayed-over-control", true, true, true, false);
        return new("candidate-only", "app-preference-and-history-checked-system-change-still-required", true, true, true, true);
    }
}
