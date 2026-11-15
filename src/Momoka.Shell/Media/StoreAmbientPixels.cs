using Momoka.Theming;

namespace Momoka.Shell.Media;

/// <summary>页面所有的单帧缓存；图像与有效主题共同决定结果，离页后释放对原图的引用。</summary>
internal sealed class StoreAmbientPixels
{
    private HeroPictureLoader.HeroPixels? _pixels;
    private UiTheme? _theme;
    private byte[]? _frame;

    internal byte[] Resolve(HeroPictureLoader.HeroPixels pixels, UiTheme theme)
    {
        if (ReferenceEquals(_pixels, pixels) && _theme == theme && _frame is not null) return _frame;
        var frame = ArtworkAmbient.Render(pixels.Pixels, pixels.Width, pixels.Height, theme.Colors.Window, theme.IsDark);
        _pixels = pixels;
        _theme = theme;
        return _frame = frame;
    }

    internal void Clear()
    {
        _pixels = null;
        _theme = null;
        _frame = null;
    }
}
