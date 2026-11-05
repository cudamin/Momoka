#if CARD_IMAGE_RESIZE_FIXTURE
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Media
{
    internal static class HeroPictureLoader
    {
        public sealed record HeroPixels(byte[] Pixels, int Width, int Height);
    }
    // 本工程没有 WinUI 解码器。这里记录 BannerSlide 要了什么，并交出与生产 BMP 同尺寸的假位图；
    // 真正的 WIC 解码、扩边和 BMP 编码由实机 UI 计时验证，不由这个边界冒充。
    internal static class BannerPictureLoader
    {
        public static int Decodes { get; private set; }
        public static List<(string Bytes, int MaxWidth)> Requests { get; } = [];
        public static TaskCompletionSource<bool>? HoldRender { get; set; }
        public static Func<bool, Task>? HoldByMode { get; set; }
        public static bool RenderFails { get; set; }
        public static int Renders { get; private set; }

        public static Task<HeroPictureLoader.HeroPixels?> DecodePixelsAsync(byte[] bytes, int maxWidth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Decodes++;
            Requests.Add((Encoding.UTF8.GetString(bytes), maxWidth));
            return Task.FromResult<HeroPictureLoader.HeroPixels?>(new(bytes, maxWidth, Math.Max(1, maxWidth * 9 / 16)));
        }

        public static async Task<BitmapImage?> RenderAsync(HeroPictureLoader.HeroPixels pixels, bool gaussian, CancellationToken token)
        {
            Renders++;
            if (HoldByMode is { } byMode) await byMode(gaussian);
            if (HoldRender is { } hold) await hold.Task;
            token.ThrowIfCancellationRequested();
            if (RenderFails) throw new InvalidDataException("anonymous rendering failure");
            return new BitmapImage
            {
                DecodePixelWidth = pixels.Width,
                PixelWidth = pixels.Width,
                PixelHeight = pixels.Height,
                FixtureImage = Encoding.UTF8.GetString(pixels.Pixels) + (gaussian ? "|gaussian" : "")
            };
        }

        public static Task<BitmapImage?> DecodeAsync(byte[] bytes, int maxWidth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Decodes++;

            var image = Encoding.UTF8.GetString(bytes);
            Requests.Add((image, maxWidth));

            var width = Math.Max(1, maxWidth);
            return Task.FromResult<BitmapImage?>(new BitmapImage
            {
                DecodePixelWidth = width,
                PixelWidth = width,
                PixelHeight = Math.Max(1, width * 9 / 16),
                FixtureImage = image
            });
        }

        public static void Reset()
        {
            Decodes = 0;
            Renders = 0;
            HoldRender = null;
            HoldByMode = null;
            RenderFails = false;
            Requests.Clear();
        }
    }
}
#endif
