using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 图标换了状态时轻轻弹一下（1 → 1.14 → 1）。只动 RenderTransform 的缩放，不碰布局、可见性与画刷。
/// <para>
/// 目标元素自己摆过别的 Transform 时不抢：那一档安静地换字形，不弹。
/// 界面动画关着、元素不在树上或还没量到尺寸时什么都不做，点击照常落到命令上。
/// </para>
/// </summary>
internal static class GlyphPulse
{
    /// <summary>单程时长；往返共 180ms，短到不挡下一次点击。</summary>
    internal const int HalfMilliseconds = 90;
    internal const double Peak = 1.14;
    private static readonly UISettings SystemSettings = new();
    private static readonly ConditionalWeakTable<FrameworkElement, PulseMotion> Active = new();

    internal static bool Play(FrameworkElement? target)
    {
        if (target is null) return false;
        Stop(target);
        if (target.XamlRoot is null || !HomeMotion.AnimationsEnabled || target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return false;

        var scale = ScaleFor(target);
        if (scale is null) return false;

        // 连续点击时从头再弹；先复位，AutoReverse 才会准确地回到 1。
        scale.ScaleX = 1;
        scale.ScaleY = 1;
        scale.CenterX = target.ActualWidth / 2;
        scale.CenterY = target.ActualHeight / 2;

        var duration = new Duration(TimeSpan.FromMilliseconds(HalfMilliseconds));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var board = new Storyboard();
        board.Children.Add(Pulse(scale, "ScaleX", duration, ease));
        board.Children.Add(Pulse(scale, "ScaleY", duration, ease));
        var motion = new PulseMotion(target, scale, board);
        Active.Add(target, motion);
        return motion.Start();
    }

    /// <summary>离树、再次点击与动画偏好改变共用；仅清理本次脉冲拥有的缩放。</summary>
    private static void Stop(FrameworkElement target)
    {
        if (!Active.TryGetValue(target, out var motion)) return;
        Active.Remove(target);
        motion.Stop();
    }

    private static DoubleAnimation Pulse(ScaleTransform scale, string property, Duration duration, CubicEase ease)
    {
        var grow = new DoubleAnimation
        {
            From = 1,
            To = Peak,
            Duration = duration,
            AutoReverse = true,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(grow, scale);
        Storyboard.SetTargetProperty(grow, property);
        return grow;
    }

    private static ScaleTransform? ScaleFor(FrameworkElement target)
    {
        if (target.RenderTransform is ScaleTransform existing) return existing;
        if (target.RenderTransform is not null) return null;

        var scale = new ScaleTransform();
        target.RenderTransform = scale;
        return scale;
    }

    private sealed class PulseMotion
    {
        private readonly FrameworkElement _target;
        private readonly ScaleTransform _scale;
        private readonly Storyboard _board;
        private readonly EventHandler<object> _completed;
        private bool _stopped;

        internal PulseMotion(FrameworkElement target, ScaleTransform scale, Storyboard board)
        {
            _target = target;
            _scale = scale;
            _board = board;
            _completed = (_, _) => Finish();
        }

        internal bool Start()
        {
            _board.Completed += _completed;
            _target.Unloaded += OnUnloaded;
            HomeMotion.PreferenceChanged += ApplyPreference;
            try
            {
                SystemSettings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
                _board.Begin();
                return true;
            }
            catch (Exception)
            {
                Finish();
                return false;
            }
        }

        private void Finish()
        {
            // 已投递的旧 Completed 不得停止下一次点击启动的脉冲。
            if (!_stopped && Active.TryGetValue(_target, out var current) && ReferenceEquals(current, this))
                GlyphPulse.Stop(_target);
        }

        internal void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _board.Completed -= _completed;
            _target.Unloaded -= OnUnloaded;
            HomeMotion.PreferenceChanged -= ApplyPreference;
            try { SystemSettings.AnimationsEnabledChanged -= OnSystemPreferenceChanged; }
            catch (Exception)
            {
                // 原生退订失败不保证目标已释放；_stopped 仍让遗留回调无权操作下一次脉冲。
            }
            _board.Stop();
            _scale.ScaleX = 1;
            _scale.ScaleY = 1;
        }

        private void OnUnloaded(object sender, RoutedEventArgs args) => Finish();

        private void ApplyPreference()
        {
            if (!HomeMotion.AnimationsEnabled) Finish();
        }

        private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        {
            if (_target.DispatcherQueue.HasThreadAccess) ApplyPreference();
            else _target.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyPreference);
        }
    }
}
