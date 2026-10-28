namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

// 只替代图片解码边界；清单、选择、分组和通知执行实际生产源码。
namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage { }
}

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> DecodeAsync(byte[] bytes, int width) =>
            Task.FromResult(new Microsoft.UI.Xaml.Media.Imaging.BitmapImage());
    }
}
