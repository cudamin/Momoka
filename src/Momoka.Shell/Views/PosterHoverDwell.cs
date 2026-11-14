namespace Momoka.Shell.Views;

/// <summary>封面放大只认连续静止的半秒；单调时间由调用者传入，旧计时回调不能提前触发。</summary>
internal sealed class PosterHoverDwell
{
    internal const int DelayMs = 500;
    private (int X, int Y) _position;
    private long _started;
    internal bool IsPending { get; private set; }
    internal bool IsReady { get; private set; }
    internal bool IsSuppressed { get; private set; }

    /// <summary>真正移动才重新计时；放大后在封面内移动继续保持，直到取消。</summary>
    internal bool Observe(int x, int y, long now)
    {
        if (IsSuppressed || IsReady || (IsPending && _position == (x, y))) return false;
        _position = (x, y);
        _started = now;
        IsPending = true;
        return true;
    }

    internal long RemainingMilliseconds(long now) => Math.Max(0, DelayMs - (now - _started));

    internal bool TryComplete(long now)
    {
        if (!IsPending || RemainingMilliseconds(now) > 0) return false;
        IsPending = false;
        IsReady = true;
        return true;
    }

    internal void Cancel() => IsPending = IsReady = false;

    /// <summary>点击后的移动、焦点和捕获事件都不能重新触发；真正离开或换卡才解除。</summary>
    internal void Suppress()
    {
        Cancel();
        IsSuppressed = true;
    }

    internal void Reset()
    {
        Cancel();
        IsSuppressed = false;
    }
}
