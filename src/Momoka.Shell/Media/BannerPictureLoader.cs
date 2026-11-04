using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell.Media;

/// <summary>将采色延长区和轮播原图一起解码，交叉淡入时两部分始终属于同一张图。</summary>
internal static class BannerPictureLoader
{
    public static async Task<BitmapImage?> DecodeAsync(byte[] bytes, int maxWidth, CancellationToken token)
    {
        var pixels = await DecodePixelsAsync(bytes, maxWidth, token).ConfigureAwait(true);
        return pixels is null ? null : await RenderAsync(pixels, false, token).ConfigureAwait(true);
    }

    public static async Task<HeroPictureLoader.HeroPixels?> DecodePixelsAsync(byte[] bytes, int maxWidth, CancellationToken token)
    {
        if (bytes.Length == 0) return null;
        token.ThrowIfCancellationRequested();

        using var source = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(source))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        source.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(source);
        token.ThrowIfCancellationRequested();

        // 在解码时限制尺寸；尊重照片方向，避免服务器忽略宽度参数时持有原始大图。
        var ratio = Math.Min(1d, Math.Max(1, maxWidth) / (double)decoder.OrientedPixelWidth);
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(decoder.PixelWidth * ratio)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * ratio)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        using var decoded = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        token.ThrowIfCancellationRequested();

        var width = decoded.PixelWidth;
        var height = decoded.PixelHeight;
        var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(width * height * 4)));
        decoded.CopyToBuffer(buffer);
        var pixels = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);

        // 像素采样离开 UI 线程，拖动窗口和快速翻页不必等这段循环完成。
        var extended = await Task.Run(() => CarouselBackdrop.ExtendLeft(pixels, width, height), token);
        token.ThrowIfCancellationRequested();
        return new HeroPictureLoader.HeroPixels(extended.Pixels, extended.Width, height);
    }

    public static Task<BitmapImage?> RenderAsync(HeroPictureLoader.HeroPixels pixels, bool gaussian, CancellationToken token) =>
        HeroPictureLoader.RenderAsync(pixels, gaussian ? GaussianBackdrop.Sigma : 0, token, null, gaussian);
}
