using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Momoka.Emby;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class HomeBanner
{
    private void UpdateCyclePause()
    {
        var paused = _layout == CarouselLayout.Store && _heroHover;
        if (_cycleClock.IsPaused == paused) return;

        _cycleClock.SetPaused(paused, Environment.TickCount64);
        var progress = _cycleClock.ElapsedMilliseconds(Environment.TickCount64) / _dwell.TotalMilliseconds;
        CountdownScale.ScaleX = Math.Clamp(progress, 0, 1);
        PaintCountdown(progress);
        if (paused) _timer?.Stop();
        else if (_active && _slides.Count > 1) _timer?.Start();
    }

    /// <summary>从 12 点顺时针绘制剩余时间，圆点、线性条和翻页都不另开动画。</summary>
    private void PaintCountdown(double progress)
    {
        var remaining = Math.Clamp(1 - progress, 0, 1);
        CountdownSlice.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        // 同一端点的整圆弧在原生 Path 中为空；少于一个物理像素的缺口保证第一帧仍为整圆。
        var angle = Math.Min(remaining, 0.999999) * Math.Tau;
        CountdownArc.Point = new Point(8 + 8 * Math.Sin(angle), 8 - 8 * Math.Cos(angle));
        CountdownArc.IsLargeArc = remaining > 0.5;
    }

    private void OnHeroPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        _heroWatch.Enter();
    }

    private void OnHeroPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(HeroClip).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < HeroClip.ActualWidth && point.Y < HeroClip.ActualHeight)
            return;
        _heroWatch.Leave();
    }
}
