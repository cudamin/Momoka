using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

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

    internal static bool Play(FrameworkElement? target)
    {
        if (target is null || target.XamlRoot is null || !HomeMotion.AnimationsEnabled) return false;

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
        board.Completed += (_, _) =>
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        };
        board.Begin();
        return true;
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
}
