// Only framework/platform value boundaries. No Window, login, configuration IO, bitmap decode,
// real clipboard, dispatcher, backend or network is available to this source-linked suite.
namespace Microsoft.UI.Xaml
{
    public class FrameworkElement
    {
        public List<FrameworkElement> Children { get; } = [];
        public double ActualHeight => 100;
    }
    public enum Visibility { Visible, Collapsed }
    public enum TextAlignment { Left, Center }
    public enum GridUnitType { Auto, Pixel, Star }
    public readonly record struct GridLength(double Value, GridUnitType GridUnitType);
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double value) : this(value, value, value, value) { }
    }
    public sealed class Application
    {
        public static Application? Current => null;
        public Dictionary<string, object> Resources { get; } = [];
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class TextBlock : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; }
    public class StackPanel : Microsoft.UI.Xaml.FrameworkElement;
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}
namespace Microsoft.UI.Xaml.Media
{
    public class Brush;
    public enum Stretch { None, Fill, Uniform, UniformToFill }
}
namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage;
}
namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int width) =>
            throw new InvalidOperationException("PERF 测试禁止解码／图片 IO");
    }
}
namespace Momoka.Shell.Platform
{
    public interface IUiDispatcher
    {
        bool Post(Action work);
        void Run(Action work);
    }
    internal interface IClipboard { void SetText(string text); }
    internal interface ISystemLauncher
    {
        void OpenFolder(string path);
        void OpenUrl(string url);
    }
}
namespace Momoka.Shell.Views
{
    internal interface IShellActions
    {
        void Notify(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity);
    }
}
