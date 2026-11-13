using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views;

/// <summary>Gallery Controls/OpacityMaskView.xaml.cs, v2.9.3 / 14a4a1a2.
/// 保留单次VisualSurface重定向；二维alpha图把左右与底部渐隐合在同一个遮罩里。
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

    internal StoreBackdropFade(FrameworkElement content, FrameworkElement host, Border maskElement)
    {
        _content = content;
        _host = host;
        _maskElement = maskElement;
        const int side = 128;
        var bitmap = new WriteableBitmap(side, side);
        var pixels = new byte[side * side * 4];
        for (var y = 0; y < side; y++)
        {
            var down = Interpolate((double)y / (side - 1), [(0, 1), (0.3, 1), (0.55, 0.75), (0.8, 0.22), (1, 0)]);
            for (var x = 0; x < side; x++)
            {
                var across = Interpolate((double)x / (side - 1), [(0, 0), (0.16, 0.7), (0.35, 1), (0.9, 1), (1, 0)]);
                var alpha = (byte)Math.Round(255 * down * across);
                var offset = (y * side + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = alpha;
            }
        }
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

    private static double Interpolate(double position, (double Position, double Alpha)[] stops)
    {
        for (var i = 1; i < stops.Length; i++)
        {
            if (position > stops[i].Position) continue;
            var previous = stops[i - 1];
            var next = stops[i];
            return previous.Alpha + (next.Alpha - previous.Alpha) * (position - previous.Position) / (next.Position - previous.Position);
        }
        return stops[^1].Alpha;
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
