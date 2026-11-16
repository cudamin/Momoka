using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views;

/// <summary>这块二维透明遮罩负责哪一种面。</summary>
internal enum StoreBackdropFadeShape
{
    /// <summary>剧照：左边、右边与下边渐隐，画面落在环境底上，右侧留出滚动条的空隙。</summary>
    Picture,

    /// <summary>环境底：左端贴住内容区左沿（2026-11-15 起不再起坡，侧栏旁边不留暗带）、底部收边、右端收在画布上。</summary>
    Ambient,

    /// <summary>
    /// 媒体详情页的环境底：铺满窗口后左右两端都收边（左端按右端同一条曲线），底部照旧。
    /// 订阅详情页仍用 <see cref="Ambient"/>，不在那一页加左端渐隐。
    /// </summary>
    AmbientSymmetric
}

/// <summary>Gallery Controls/OpacityMaskView.xaml.cs, v2.9.3 / 14a4a1a2.
/// 保留单次VisualSurface重定向；二维alpha图把剧照的左边、右边与底部渐隐合在同一个遮罩里，
/// 环境底只收右边与底部（左端贴住内容区左沿，见 <see cref="StoreBackdropFadeShape.Ambient"/>；
/// 媒体详情页铺满窗口后用 <see cref="StoreBackdropFadeShape.AmbientSymmetric"/>，左端也收）。
/// 剧照与环境底各用一份；环境底铺到信息区，剧照保留右侧清晰画面。
/// 微软商店闭源，截图仅作外观参考。沿用 Gallery 的重定向机制，不对其内部实现作推断。
/// </summary>
internal sealed class StoreBackdropFade
{
    private readonly FrameworkElement _content;
    private readonly FrameworkElement _host;
    private readonly Border _maskElement;
    private CompositionVisualSurface? _surface;
    private CompositionSurfaceBrush? _source;
    private CompositionVisualSurface? _maskSurface;
    private CompositionSurfaceBrush? _maskSource;
    private CompositionMaskBrush? _mask;
    private SpriteVisual? _output;
    internal bool IsActive => _output is not null;

    // 2026-11-15 用户令「背景图/预告片下方和左右要加渐变」：左右两端都收到环境底，画面不再有一条贴着
    // 滚动条的硬边；右端留的那道空隙（StorePreviewGap）与这段渐隐一起把视口收进页面。
    internal StoreBackdropFade(FrameworkElement content, FrameworkElement host, Border maskElement,
        StoreBackdropFadeShape shape = StoreBackdropFadeShape.Picture)
    {
        _content = content;
        _host = host;
        _maskElement = maskElement;
        const int side = StoreBackdropBlend.Side;
        var bitmap = new WriteableBitmap(side, side);
        var pixels = StoreBackdropBlend.Mask(shape != StoreBackdropFadeShape.Picture,
            symmetric: shape == StoreBackdropFadeShape.AmbientSymmetric);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
        bitmap.Invalidate();
        maskElement.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
        ElementCompositionPreview.GetElementVisual(maskElement).Opacity = 0;
        host.Loaded += (_, _) => Connect();
        host.Unloaded += (_, _) => Disconnect();
        host.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (host.Visibility == Visibility.Visible && host.IsLoaded) Connect();
            else Disconnect();
        });
    }

    private void Connect()
    {
        if (_output is not null || _host.Visibility != Visibility.Visible) return;
        var compositor = ElementCompositionPreview.GetElementVisual(_content).Compositor;
        try
        {
            (_surface, _source) = GetVisualBrush(_content);
            (_maskSurface, _maskSource) = GetVisualBrush(_maskElement);
            _mask = compositor.CreateMaskBrush();
            _mask.Source = _source;
            _mask.Mask = _maskSource;
            _output = compositor.CreateSpriteVisual();
            _output.RelativeSizeAdjustment = Vector2.One;
            _output.Brush = _mask;
            ElementCompositionPreview.SetElementChildVisual(_host, _output);
        }
        catch { Disconnect(); throw; }
    }

    private static (CompositionVisualSurface Surface, CompositionSurfaceBrush Brush) GetVisualBrush(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var surface = compositor.CreateVisualSurface();
        surface.SourceVisual = visual;
        using var animation = compositor.CreateExpressionAnimation("visual.Size");
        animation.SetReferenceParameter("visual", visual);
        surface.StartAnimation(nameof(surface.SourceSize), animation);
        var brush = compositor.CreateSurfaceBrush(surface);
        visual.Opacity = 0;
        return (surface, brush);
    }

    private void Disconnect()
    {
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        ElementCompositionPreview.GetElementVisual(_content).Opacity = 1;
        ElementCompositionPreview.GetElementVisual(_maskElement).Opacity = 0;
        if (_mask is not null) { _mask.Source = null; _mask.Mask = null; }
        if (_surface is not null) _surface.SourceVisual = null;
        if (_maskSurface is not null) _maskSurface.SourceVisual = null;
        _output?.Dispose();
        _mask?.Dispose();
        _maskSource?.Dispose();
        _maskSurface?.Dispose();
        _source?.Dispose();
        _surface?.Dispose();
        _output = null;
        _mask = null;
        _maskSource = null;
        _maskSurface = null;
        _source = null;
        _surface = null;
    }
}
