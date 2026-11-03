#if CARD_IMAGE_RESIZE_FIXTURE
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Media
{
    // 本工程没有 WinUI 解码器。这里记录 BannerSlide 要了什么，并交出与生产 BMP 同尺寸的假位图；
    // 真正的 WIC 解码、扩边和 BMP 编码由实机 UI 计时验证，不由这个边界冒充。
    internal static class BannerPictureLoader
    {
        public static int Decodes { get; private set; }
        public static List<(string Bytes, int MaxWidth)> Requests { get; } = [];

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
            Requests.Clear();
        }
    }
}
#endif
