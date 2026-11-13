using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 常驻元素与浮层的短进出：只动 Opacity 与 RenderTransform 的 Y，不碰布局、可见性与其它属性。
/// <para>
/// 打断从当前不透明度与位移接上，不闪回起点；第一次显示从下方轻推一段。系统或设置关掉界面动画、
/// 元素不在树上时立即落定 —— 调用方照常按「该显示/该收起」调用，不必自己判断开关。
/// </para>
/// <para>
/// 退出完成后元素停在不可见那一档（Opacity 0），由调用方的落定回调收起 Visibility。
/// <see cref="Settle"/> 与 <see cref="Collapse"/> 分别把值复位到「显示着」与「收起」两档，供页面释放、
/// 重新上树或跳过动画的路径使用；两档都让下一次 <see cref="Enter"/> 重新从下方淡入。
/// </para>
/// </summary>
internal sealed class SurfaceMotion
{
    internal const int EnterMilliseconds = 180;
    internal const int ExitMilliseconds = 140;
    internal const double EnterOffset = 12;
    internal const double ExitOffset = 8;

    private static readonly UISettings SystemSettings = new();
    private readonly FrameworkElement _element;
    private Storyboard? _board;
    private EventHandler<object>? _completed;
    private Action? _landed;
    private double _finalOpacity;
    private double _finalY;
    private int _generation;
    private bool _shown;
    private bool _watchingPreferences;

    internal SurfaceMotion(FrameworkElement element)
    {
        _element = element;
        element.Unloaded += OnUnloaded;
    }

    internal bool IsAnimating => _board is not null;

    /// <summary>元素此刻是不是停在「显示着」那一档；重复显示不必重播。</summary>
    internal bool IsShown => _shown;

    internal void Enter(int milliseconds = EnterMilliseconds, double offset = EnterOffset)
    {
        if (milliseconds <= 0 || !HomeMotion.AnimationsEnabled || _element.XamlRoot is null)
        {
            Settle();
            return;
        }

        // 已经在显示档（含正在淡入）：重复打开不该把它从头再淡一遍。
        if (_shown) return;

        var drift = DriftFor(_element);
        var opacity = _element.Opacity;
        if (opacity >= 1 && (drift is null || drift.Y == 0))
        {
            // 第一次显示：元素刚从 Collapsed 变回可见，还没有画过一帧，这一步不会闪。
            _element.Opacity = 0;
            opacity = 0;
            if (drift is not null) drift.Y = offset;
        }

        _shown = true;
        Play(opacity, 1, drift, drift?.Y ?? 0, 0, milliseconds, EasingMode.EaseOut, null);
    }

    internal void Exit(int milliseconds = ExitMilliseconds, double offset = ExitOffset, Action? landed = null)
    {
        if (milliseconds <= 0 || !HomeMotion.AnimationsEnabled || _element.XamlRoot is null)
        {
            Collapse();
            landed?.Invoke();
            return;
        }

        if (!_shown && !IsAnimating)
        {
            // 本来就没显示：把调用方的收尾照常做完，不凭空播一段。
            landed?.Invoke();
            return;
        }

        _shown = false;
        var drift = DriftFor(_element);
        Play(_element.Opacity, 0, drift, drift?.Y ?? 0, offset, milliseconds, EasingMode.EaseIn, landed);
    }

    /// <summary>停掉动画，复位到「显示着」；待落定的回调作废。</summary>
    internal void Settle() => Reset(shown: true);

    /// <summary>停掉动画，复位到「收起」；待落定的回调作废。元素本身的 Visibility 由调用方收起。</summary>
    internal void Collapse() => Reset(shown: false);

    private void Reset(bool shown)
    {
        Cancel();
        _shown = shown;
        _element.Opacity = 1;
        if (_element.RenderTransform is TranslateTransform drift) drift.Y = 0;
    }

    private void Play(double fromOpacity, double toOpacity, TranslateTransform? drift, double fromY, double toY,
        int milliseconds, EasingMode mode, Action? landed)
    {
        Cancel();
        var generation = _generation;
        _landed = landed;
        _finalOpacity = toOpacity;
        _finalY = toY;

        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));
        var ease = new CubicEase { EasingMode = mode };
        var board = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = fromOpacity,
            To = toOpacity,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(fade, _element);
        Storyboard.SetTargetProperty(fade, "Opacity");
        board.Children.Add(fade);

        if (drift is not null)
        {
            var rise = new DoubleAnimation
            {
                From = fromY,
                To = toY,
                Duration = duration,
                EasingFunction = ease,
            };
            Storyboard.SetTarget(rise, drift);
            Storyboard.SetTargetProperty(rise, "Y");
            board.Children.Add(rise);
        }

        _board = board;
        _completed = (_, _) =>
        {
            if (generation == _generation && ReferenceEquals(_board, board)) Finish();
        };
        board.Completed += _completed;
        try
        {
            WatchPreferences();
            board.Begin();
        }
        catch (Exception)
        {
            // 关闭窗口或离树可能使平台拒绝启动；落定与取消的语义仍由当前调用决定。
            Finish();
        }
    }

    private void Finish()
    {
        var landed = _landed;
        var opacity = _finalOpacity;
        var y = _finalY;
        Cancel();
        _element.Opacity = opacity;
        if (_element.RenderTransform is TranslateTransform drift) drift.Y = y;
        landed?.Invoke();
    }

    private void Cancel()
    {
        var board = _board;
        _board = null;
        if (board is not null && _completed is not null) board.Completed -= _completed;
        _completed = null;
        _landed = null;
        _generation++;
        StopWatchingPreferences();
        if (board is null) return;

        // 先把动画值抄回本地值，Stop 之后不跳回起点（打断时新动画从这里接上）。
        var opacity = _element.Opacity;
        var drift = _element.RenderTransform as TranslateTransform;
        var y = drift?.Y ?? 0;
        board.Stop();
        _element.Opacity = opacity;
        if (drift is not null) drift.Y = y;
    }

    private void WatchPreferences()
    {
        if (_watchingPreferences) return;
        _watchingPreferences = true;
        HomeMotion.PreferenceChanged += ApplyPreference;
        SystemSettings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
    }

    private void StopWatchingPreferences()
    {
        if (!_watchingPreferences) return;
        _watchingPreferences = false;
        HomeMotion.PreferenceChanged -= ApplyPreference;
        try { SystemSettings.AnimationsEnabledChanged -= OnSystemPreferenceChanged; }
        catch (Exception)
        {
            // 平台拒绝退订不应中断本地时钟和终值清理；已排队回调由代次与空 _board 拒绝。
        }
    }

    private void ApplyPreference()
    {
        if (_board is not null && !HomeMotion.AnimationsEnabled) Finish();
    }

    private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        var generation = _generation;
        void ApplyCurrentPreference()
        {
            if (generation == _generation) ApplyPreference();
        }
        if (_element.DispatcherQueue.HasThreadAccess) ApplyCurrentPreference();
        else _element.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyCurrentPreference);
    }

    /// <summary>
    /// 位移用的 Transform：没有 RenderTransform 就装一支 TranslateTransform，已有 TranslateTransform
    /// 就接着用，别的 Transform（卡片自己摆的缩放之类）不抢 —— 那一档只淡入、不上浮。
    /// </summary>
    private static TranslateTransform? DriftFor(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform existing) return existing;
        if (element.RenderTransform is not null) return null;

        var drift = new TranslateTransform();
        element.RenderTransform = drift;
        return drift;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Repeater 回收的幻影仍带着 XamlRoot；只有真离树才把值复位，免得下次上来是透明的。
        if (_element.XamlRoot is not null) return;
        Reset(shown: _shown);
    }
}
