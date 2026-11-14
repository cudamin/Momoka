using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 封面鼠标附近的柔光。使用 Gallery 的原生径向画刷，不改变剧照的缩放、读秒或输入目标。
/// Absolute 坐标以 DIP 为单位，宽窄窗和 DPI 改变时光仍为圆形，不随封面拉成椭圆。
/// </summary>
internal sealed class BannerPointerGlow
{
    private readonly FrameworkElement _surface;
    private readonly FrameworkElement _bounds;
    private readonly RadialGradientBrush _brush;
    private readonly SurfaceMotion _motion;
    private bool _enabled;
    private bool _hasPointer;
    private Point _point;

    internal BannerPointerGlow(FrameworkElement surface, RadialGradientBrush brush, FrameworkElement bounds)
    {
        _surface = surface;
        _bounds = bounds;
        _brush = brush;
        _motion = new SurfaceMotion(surface);
    }

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) Hide(immediate: true);
        else if (_hasPointer) Show();
    }

    internal void Move(Point point, bool mouse)
    {
        if (!mouse || !Contains(point))
        {
            Leave();
            return;
        }

        _point = point;
        _hasPointer = true;
        if (_enabled) Show();
    }

    internal void Leave(bool immediate = false)
    {
        if (!_hasPointer && !immediate) return;
        _hasPointer = false;
        Hide(immediate);
    }

    private bool Contains(Point point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y)
        && point.X >= 0 && point.Y >= 0
        // 光层初始 Collapsed，尺寸为零；判定必须来自已布局的封面，才能首次点亮。
        && point.X < _bounds.ActualWidth && point.Y < _bounds.ActualHeight;

    private void Show()
    {
        if (!Contains(_point))
        {
            Leave(immediate: true);
            return;
        }

        // Gallery RadialGradientBrushPage.OnSliderValueChanged 的同一对坐标赋值；
        // 没有自动追逐动画，鼠标点和光心始终一致。
        _brush.Center = _point;
        _brush.GradientOrigin = _point;
        _surface.Visibility = Visibility.Visible;
        _motion.Enter(offset: 0);
    }

    private void Hide(bool immediate)
    {
        if (immediate)
        {
            _motion.Collapse();
            _surface.Visibility = Visibility.Collapsed;
            return;
        }

        _motion.Exit(offset: 0, landed: () => _surface.Visibility = Visibility.Collapsed);
    }
}
