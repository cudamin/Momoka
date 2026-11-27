using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>只缓存当前视频视口的叠图，主题、剧照和实际布局变动时重建。</summary>
internal sealed class StoreVideoBrush
{
    private byte[]? _ambient;
    private (ThemeColor Window, double Page, double Ambient, double Width, double Height, double Gap,
        double ShadeWidth, double ShadeHeight, bool Dark) _layout;
    private ImageBrush? _brush;
    private (ThemeColor Window, double Page, double Width, double Height, double Gap) _foundationLayout;
    private ImageBrush? _foundation;

    internal void Clear() { _ambient = null; _brush = null; _foundation = null; }

    internal ImageBrush ResolveFoundation(ThemeColor window, double page, double width, double height, double gap)
    {
        var layout = (window, page, width, height, gap);
        if (_foundation is not null && _foundationLayout == layout) return _foundation;
        var bitmap = new WriteableBitmap(StoreBackdropBlend.FoundationWidth, StoreBackdropBlend.FoundationHeight);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(StoreBackdropBlend.Foundation(window, page, width, height, gap));
        bitmap.Invalidate();
        _foundationLayout = layout;
        return _foundation = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
    }

    internal ImageBrush Resolve(byte[]? ambient, ThemeColor window, double page, double ambientHeight,
        double width, double height, double gap, double shadeWidth, double shadeHeight, bool dark)
    {
        var layout = (window, page, ambientHeight, width, height, gap, shadeWidth, shadeHeight, dark);
        if (_brush is not null && ReferenceEquals(_ambient, ambient) && _layout == layout) return _brush;
        var bitmap = new WriteableBitmap(StoreBackdropBlend.Side, StoreBackdropBlend.Side);
        var pixels = StoreBackdropBlend.Video(ambient, window, page, ambientHeight, width, height, gap,
            shadeWidth, shadeHeight, dark);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
        bitmap.Invalidate();
        _ambient = ambient;
        _layout = layout;
        return _brush = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
    }
}
