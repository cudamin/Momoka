namespace Momoka.Shell;

internal readonly record struct MotionCaptureTiming(int Index, double StartMs, double EndMs,
    double CaptureMs, double? IntervalMs, int ThreadId);

internal sealed record MotionCaptureRegion(MotionMarkerRegion Area, double MeasuredAtMs);

/// <summary>专用后台线程连续取实际屏幕；捕获与读数回调不得访问XAML，停止后先等本次捕获退出再清页面。</summary>
internal static class MotionScreenSampler
{
    internal static Task RunAsync<T>(Func<T> capture, Action<T, MotionCaptureTiming> observe,
        Func<double> elapsed, int durationMs, int intervalMs, CancellationToken stopping) =>
        Task.Factory.StartNew(() =>
        {
            if (durationMs <= 0 || intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
            var index = 0;
            double? previousStart = null;
            while (!stopping.IsCancellationRequested)
            {
                var start = elapsed();
                var pixels = capture();
                var end = elapsed();
                if (stopping.IsCancellationRequested) break;
                if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end < start)
                    throw new InvalidOperationException("动效实际捕获时钟无效");
                observe(pixels, new(index++, start, end, end - start,
                    previousStart is { } previous ? start - previous : null, Environment.CurrentManagedThreadId));
                previousStart = start;
                if (end >= durationMs) break;
                // 只在后台等到下一次采样；慢捕获不再额外补满16ms，捕获与开始间隔分别留证。
                var delay = Math.Max(0, intervalMs - (elapsed() - start));
                if (stopping.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(delay))) break;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}

internal sealed record MotionDisableDispatchRead(double? RequestedAtMs, double? AppliedAtMs,
    bool Accepted, bool Stopped, string? FailureType, string? FailureMessage);

/// <summary>后台只请求关闭；真实UI回调执行成功后才发布Applied，停止后的迟到回调不能动下一页。</summary>
internal sealed class MotionDisableDispatch(Func<Action, bool> enqueue, Action apply, Func<double> elapsed)
{
    private readonly object _gate = new();
    private bool _active = true;
    private double? _requested;
    private double? _applied;
    private bool _accepted;
    private bool _applying;
    private Exception? _failure;

    internal MotionDisableDispatchRead Snapshot
    {
        get
        {
            lock (_gate) return new(_requested, _applied, _accepted, !_active,
                _failure?.GetType().Name, _failure?.Message);
        }
    }

    internal void Request()
    {
        lock (_gate)
        {
            if (!_active || _requested is not null) return;
            _requested = elapsed();
        }
        try
        {
            var accepted = enqueue(Apply);
            lock (_gate)
            {
                _accepted = accepted;
                if (!accepted) _failure = new InvalidOperationException("动效中途关闭请求未进入UI队列");
            }
        }
        catch (Exception error) { lock (_gate) _failure = error; }
        ThrowIfFailed();
    }

    private void Apply()
    {
        lock (_gate)
        {
            if (!_active || _applying || _applied is not null) return;
            _applying = true;
        }
        // UI动作不能持有读数锁；否则慢属性回调会把后台捕获再次卡在Snapshot上。
        try
        {
            apply();
            lock (_gate) _applied = elapsed();
        }
        catch (Exception error) { lock (_gate) _failure = error; }
    }

    internal void Stop() { lock (_gate) _active = false; }

    internal void ThrowIfFailed()
    {
        lock (_gate)
            if (_failure is { } failure)
                throw new InvalidOperationException("动效中途关闭实际调度失败", failure);
    }
}
