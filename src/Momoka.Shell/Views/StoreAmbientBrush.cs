using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Shell.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>本页只保留当前剧照和主题的一张 24 KiB 环境底；换主题重新生成，缩放复用。</summary>
internal sealed class StoreAmbientBrush
{
    private readonly StoreAmbientPixels _cache = new();
    private byte[]? _frame;
    private ImageBrush? _brush;
    internal byte[]? Frame => _frame;

    internal void Clear()
    {
        _cache.Clear();
        _frame = null;
        _brush = null;
    }

    internal Brush Resolve(HeroPictureLoader.HeroPixels? pixels, bool visible, UiTheme theme)
    {
        if (!visible || pixels is null)
        {
            Clear();
            return new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Window));
        }
        var frame = _cache.Resolve(pixels, theme);
        if (ReferenceEquals(_frame, frame) && _brush is not null) return _brush;
        var bitmap = new WriteableBitmap(ArtworkAmbient.Width, ArtworkAmbient.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(frame);
        bitmap.Invalidate();
        _frame = frame;
        return _brush = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
    }
}
