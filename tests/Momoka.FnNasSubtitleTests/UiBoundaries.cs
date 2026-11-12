using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Dark, Light }
    public enum FocusState { Programmatic }
    public sealed class RoutedEventArgs : EventArgs;
    public sealed class XamlRootChangedEventArgs : EventArgs;
    public readonly record struct RootSize(double Width, double Height);
    public sealed class XamlRoot
    {
        public RootSize Size { get; set; } = new(1280, 900);
        public event Action<XamlRoot, XamlRootChangedEventArgs>? Changed;
        internal void RaiseChanged() => Changed?.Invoke(this, new());
    }
    public class FrameworkElement
    {
        public object? DataContext { get; set; }
        public XamlRoot? XamlRoot { get; set; }
        public double Width { get; set; }
        public double MaxHeight { get; set; }
        public bool IsEnabled { get; set; } = true;
        public Visibility Visibility { get; set; } = Visibility.Visible;
        public event Action<object, RoutedEventArgs>? Loaded { add { } remove { } }
        public event Action<object, RoutedEventArgs>? Unloaded { add { } remove { } }
        public bool Focus(FocusState state) => true;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class ContentDialogClosingEventArgs : EventArgs { public bool Cancel { get; set; } }
    public sealed class ContentDialogOpenedEventArgs : EventArgs;
    public sealed class SelectionChangedEventArgs : EventArgs;
    public class ContentDialog : FrameworkElement
    {
        private readonly TaskCompletionSource _completion = new();
        public ElementTheme RequestedTheme { get; set; }
        public event Action<ContentDialog, ContentDialogOpenedEventArgs>? Opened;
        public event Action<ContentDialog, ContentDialogClosingEventArgs>? Closing;
        public event Action<object, EventArgs>? Closed;
        internal Task ShowAsync() { Opened?.Invoke(this, new()); return _completion.Task; }
        public void Hide()
        {
            if (_completion.Task.IsCompleted) return;
            var args = new ContentDialogClosingEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel) return;
            _completion.TrySetResult();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
    public sealed class Button : FrameworkElement;
    public sealed class TextBlock : FrameworkElement { public string Text { get; set; } = ""; }
    public sealed class TextBox : FrameworkElement { public string Text { get; set; } = ""; }
    public sealed class ItemsControl : FrameworkElement { public object? ItemsSource { get; set; } }
    public sealed class ComboBoxItem { public object? Content { get; set; } public object? Tag { get; set; } }
    public sealed class ComboBox : FrameworkElement
    {
        public List<object> Items { get; } = [];
        public int SelectedIndex { get; set; } = -1;
        public object? SelectedItem => Items.ElementAtOrDefault(SelectedIndex);
    }
    public sealed class ProgressRing : FrameworkElement { public bool IsActive { get; set; } }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties { public static void SetName(object element, string name) { } }
}

namespace Momoka.Shell
{
    internal static class ThemeHost
    {
        internal static Palette Current { get; } = new();
        internal sealed class Palette { internal bool IsDark => true; }
    }
}
