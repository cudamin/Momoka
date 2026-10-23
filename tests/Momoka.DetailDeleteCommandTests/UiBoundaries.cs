using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// UI values, dialog choices and unused visual decoding are boundaries only.
// Actual command method slices, ConfirmDialog and CardItem execute unchanged.
namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum TextAlignment { Left, Center }
    public enum GridUnitType { Auto, Pixel, Star }
    public enum ElementTheme { Default, Light, Dark }
    public readonly record struct GridLength(double Value, GridUnitType GridUnitType);
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double value) : this(value, value, value, value) { }
    }
    public sealed class XamlRoot;
    public class FrameworkElement
    {
        public object? DataContext { get; set; }
        public XamlRoot? XamlRoot { get; set; } = new();
    }
    public sealed class RoutedEventArgs : EventArgs;
}

namespace Microsoft.UI.Xaml.Media
{
    public enum Stretch { None, Fill, Uniform, UniformToFill }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage;
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public enum ContentDialogResult { None, Primary, Secondary }
    public enum ContentDialogButton { None, Primary, Secondary, Close }
    public class ContentControl : FrameworkElement
    {
        public object? Content { get; set; }
    }
    public sealed class Button : ContentControl;
    public class ContentDialog : ContentControl
    {
        public ElementTheme RequestedTheme { get; set; }
        public object? Title { get; set; }
        public string PrimaryButtonText { get; set; } = "";
        public string CloseButtonText { get; set; } = "";
        public ContentDialogButton DefaultButton { get; set; }
        public Task<ContentDialogResult> ShowAsync() => Momoka.Tests.DialogPlan.Current.Show(this);
    }

}

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        public static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int width) =>
            throw new Momoka.Tests.AssertionException("禁止图片解码/native");
    }
}

namespace Momoka.Shell.ViewModels
{
    internal delegate Task<bool> ConfirmRequest(string title, string message, string primary);
}

namespace Momoka.Shell.Views
{
    internal static class ThemeHost
    {
        public static ThemeValue Current { get; } = new();
        internal sealed class ThemeValue
        {
            public bool IsDark => true;
        }
    }
    public sealed class FnNasVersionsDialog(string itemName, IReadOnlyList<FnNasVersion> versions) : ContentDialog
    {
        public string ItemName { get; } = itemName;
        public IReadOnlyList<FnNasVersion> Versions { get; } = versions;
        public FnNasVersion? Selected { get; set; }
    }
    // Menu placement/visuals are a boundary; dispatch below enters the verbatim Invoke slice.
    internal static partial class ItemCommands
    {
        internal static ItemCommand NextCommand { get; set; } = ItemCommand.Delete;
        internal static void Show(EmbySession session, IShellActions shell, FrameworkElement owner, CardItem card,
            Windows.Foundation.Point? position, IReadOnlyList<EmbyItem>? siblings = null, Action? changed = null,
            Action<CardItem>? removed = null, Action<CardItem>? watched = null)
        {
            Invoke(NextCommand, shell, owner, card, siblings, changed, removed, watched);
        }
    }
    // Only forwards view-model calls to the real VM from the supplied fresh Shell DLL.
    internal sealed partial class DetailPage
    {
        private readonly EmbySession? _session;
        private readonly IShellActions? _actions;
        private readonly EmbyImageStore? _images;
        internal Momoka.Tests.DetailVmBridge ViewModel { get; }
        internal DetailPage(EmbySession session, IShellActions actions, EmbyImageStore images, Momoka.Tests.DetailVmBridge vm)
        {
            _session = session;
            _actions = actions;
            _images = images;
            ViewModel = vm;
        }
        internal void OpenMore(FrameworkElement owner) => OnMoreClicked(owner, new RoutedEventArgs());
    }
}
