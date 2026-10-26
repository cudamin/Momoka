using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Media;

internal static class PosterLoader
{
    public static Task<BitmapImage?> DecodeAsync(byte[] bytes, int width) =>
        throw new InvalidOperationException("R041 fixture forbids native/image decoding");
}
