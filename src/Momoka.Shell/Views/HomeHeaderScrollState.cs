namespace Momoka.Shell.Views;

internal enum HomeHeaderMode { Top, Hidden, Revealed }

/// <summary>主页顶部操作条的纵向方向滞回；留在 Shell，不把纯界面状态放进 Core。</summary>
internal sealed class HomeHeaderScrollState
{
    private const double TopTolerance = 0.5;
    private const double DirectionThreshold = 12;
    private bool _enabled;
    private double _anchor;

    internal HomeHeaderMode Mode { get; private set; }

    internal void Reset(bool enabled, double offset)
    {
        _enabled = enabled;
        _anchor = Normalize(offset);
        Mode = enabled && _anchor > TopTolerance ? HomeHeaderMode.Revealed : HomeHeaderMode.Top;
    }

    internal void Update(bool enabled, double offset)
    {
        if (!double.IsFinite(offset)) return;
        offset = Normalize(offset);
        if (_enabled != enabled || !enabled || offset <= TopTolerance)
        {
            Reset(enabled, offset);
            return;
        }

        // 从这一方向的极值累计，慢慢上滚也能恢复；相同偏移和小幅抖动不会换档。
        if (Mode == HomeHeaderMode.Hidden)
        {
            _anchor = Math.Max(_anchor, offset);
            if (_anchor - offset < DirectionThreshold) return;
            Mode = HomeHeaderMode.Revealed;
        }
        else
        {
            _anchor = Math.Min(_anchor, offset);
            if (offset - _anchor < DirectionThreshold)
            {
                Mode = HomeHeaderMode.Revealed;
                return;
            }
            Mode = HomeHeaderMode.Hidden;
        }
        _anchor = offset;
    }

    private static double Normalize(double offset) => double.IsFinite(offset) ? Math.Max(0, offset) : 0;
}
