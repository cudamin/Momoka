namespace Momoka.Shell.Views;

/// <summary>
/// 轮播停留时间只累计实际运行的部分。调用者传入单调毫秒时基；悬停与恢复不重启当前这张的周期。
/// </summary>
internal sealed class BannerCycleClock
{
    private long _started;
    private long _elapsed;

    internal bool IsPaused { get; private set; }

    /// <summary>切换条目才从零计时；手动切换时鼠标仍在主图上，新条目继续保持暂停。</summary>
    internal void Restart(long now)
    {
        _started = now;
        _elapsed = 0;
    }

    /// <summary>反复 PointerMoved 或离开通知是幂等的，不能吞掉已走时间或重复扣除暂停时间。</summary>
    internal void SetPaused(bool paused, long now)
    {
        if (IsPaused == paused) return;

        if (paused) _elapsed = ElapsedMilliseconds(now);
        else _started = now;
        IsPaused = paused;
    }

    internal long ElapsedMilliseconds(long now) =>
        _elapsed + (IsPaused ? 0 : Math.Max(0, now - _started));
}
