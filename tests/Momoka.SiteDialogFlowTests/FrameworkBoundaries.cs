// Only WinUI/XAML creation, measurement and dialog completion are inert.
// The actual picker, dialog, VM, command generator and Core service execute unchanged.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom);
    public readonly record struct Size(double Width, double Height);
    public sealed class RoutedEventArgs : EventArgs { }
    public sealed class SizeChangedEventArgs(Size size) : EventArgs { public Size NewSize => size; }
    public sealed class XamlRootChangedEventArgs : EventArgs { }
    public sealed class Style { }
    public sealed class ResourceDictionary
    {
        private readonly Dictionary<object, object> _items = new();
        public List<ResourceDictionary> MergedDictionaries { get; } = [];
        public object this[object key] { set => _items[key] = value; }
        public bool TryGetValue(object key, out object? value) => _items.TryGetValue(key, out value);
    }
    public sealed class XamlRoot
    {
        public Size Size { get; private set; } = new(1280, 800);
        public event Action<XamlRoot, XamlRootChangedEventArgs>? Changed;
        internal void Resize(double width, double height)
        {
            Size = new(width, height);
            Changed?.Invoke(this, new());
        }
    }
    public class FrameworkElement
    {
        public XamlRoot? XamlRoot { get; set; }
        public ElementTheme RequestedTheme { get; set; }
        public double Width { get; set; }
        public double MaxWidth { get; set; }
        public double MaxHeight { get; set; }
        public Visibility Visibility { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public enum ContentDialogResult { None, Primary, Secondary }
    public sealed class ContentDialogOpenedEventArgs : EventArgs { }
    public sealed class ContentDialogClosedEventArgs : EventArgs { }
    public sealed class ContentDialogButtonClickEventArgs : EventArgs { public bool Cancel { get; set; } }
    public class UserControl : FrameworkElement { }
    public sealed class TextBlock : FrameworkElement { public string? Text { get; set; } }
    public sealed class Button : FrameworkElement
    {
        public Thickness Padding { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Stretch;
    }
    public sealed class ItemsRepeater : FrameworkElement { }
    public sealed class UniformGridLayout { public double MinItemWidth { get; set; } = 168; }
    public sealed class StackPanel : FrameworkElement { }
    public sealed class Grid : FrameworkElement { }
    public sealed class ScrollViewer : FrameworkElement { }

    public class ContentDialog : FrameworkElement
    {
        private TaskCompletionSource<ContentDialogResult>? _completion;
        public bool IsPrimaryButtonEnabled { get; set; }
        public Style? PrimaryButtonStyle { get; set; }
        public ResourceDictionary Resources { get; } = new();
        public event Action<ContentDialog, ContentDialogOpenedEventArgs>? Opened;
        public event Action<ContentDialog, ContentDialogClosedEventArgs>? Closed;
        public event Action<ContentDialog, ContentDialogButtonClickEventArgs>? PrimaryButtonClick;
        public Task<ContentDialogResult> ShowAsync()
        {
            _completion = new();
            Opened?.Invoke(this, new());
            return _completion.Task;
        }
        public void Hide() => Complete(ContentDialogResult.None);
        internal void PressPrimary()
        {
            var args = new ContentDialogButtonClickEventArgs();
            PrimaryButtonClick?.Invoke(this, args);
            if (!args.Cancel) Complete(ContentDialogResult.Primary);
        }
        internal void Complete(ContentDialogResult result)
        {
            Closed?.Invoke(this, new());
            _completion!.TrySetResult(result);
        }
    }
}

namespace Momoka.Shell.Views
{
    public sealed class InertBindings { public void Update() { } }
    public static class DialogDismiss
    {
        internal static Microsoft.UI.Xaml.FrameworkElement? TemplatePart(Microsoft.UI.Xaml.Controls.ContentDialog dialog, string name) =>
            dialog is MoviePilotSiteDialog site && name == "PrimaryButton" ? site.Primary : null;
    }
    public sealed partial class MoviePilotSitePicker
    {
        internal readonly Microsoft.UI.Xaml.Controls.TextBlock SiteLabel = new();
        internal readonly Microsoft.UI.Xaml.Controls.Button ChooseSites = new();
        internal readonly InertBindings Bindings = new();
        private void InitializeComponent() { }
    }

    public sealed partial class MoviePilotSiteDialog
    {
        internal readonly Microsoft.UI.Xaml.Controls.ItemsRepeater SiteChoices = new();
        internal readonly Microsoft.UI.Xaml.Controls.UniformGridLayout SiteGridLayout = new();
        internal readonly Microsoft.UI.Xaml.Controls.StackPanel SiteDialogBody = new();
        internal readonly Microsoft.UI.Xaml.Controls.Grid SiteDialogHeader = new();
        internal readonly Microsoft.UI.Xaml.Controls.ScrollViewer SiteDialogScroll = new();
        internal readonly Microsoft.UI.Xaml.Controls.Button Primary = new();
        private void InitializeComponent()
        {
            Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.ResourceDictionary
            {
                ["SiteDialogConfirmStyle"] = new Microsoft.UI.Xaml.Style()
            });
            Opened += OnOpened;
            Closed += OnClosed;
            PrimaryButtonClick += OnConfirm;
        }
        internal void PressX() => OnClose(this, new());
        internal void SetGridWidth(double width) => OnGridSizeChanged(this, new(new(width, 100)));
    }
}
