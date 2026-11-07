using Microsoft.UI.Xaml;
using Momoka.Shell.Media;
using Momoka.Theming;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>只把已布好的控件区域映射到现有 BGRA 原图；不截图、重新解码或请求图片。</summary>
internal static class BackdropInk
{
    internal static ThemeColor Sample(FrameworkElement node, FrameworkElement picture,
        HeroPictureLoader.HeroPixels? pixels, bool alignTop, ThemeColor fallback)
    {
        if (pixels is null || node.ActualWidth <= 0 || node.ActualHeight <= 0) return fallback;
        try
        {
            var origin = node.TransformToVisual(picture).TransformPoint(new Point(0, 0));
            return BackdropReading.Sample(pixels.Pixels, pixels.Width, pixels.Height,
                picture.ActualWidth, picture.ActualHeight, origin.X, origin.Y,
                node.ActualWidth, node.ActualHeight, alignTop, fallback);
        }
        catch (ArgumentException) { return fallback; }
    }
}
