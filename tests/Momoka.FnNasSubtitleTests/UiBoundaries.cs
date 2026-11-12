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
        private XamlRoot? _xamlRoot;
        private int _uiThread;
        private readonly DispatcherQueue _dispatcher = new();
        public object? DataContext { get; set; }
        internal FrameworkElement? Parent { get; set; }
        internal int WrongThreadReads { get; private set; }
        internal void RestrictToCurrentThread() => _uiThread = Environment.CurrentManagedThreadId;
        public XamlRoot? XamlRoot
        {
            get
            {
                if (_uiThread != 0 && Environment.CurrentManagedThreadId != _uiThread)
                {
                    WrongThreadReads++;
                    throw new InvalidOperationException("RPC_E_WRONG_THREAD: XamlRoot accessed off UI thread");
                }
                return _xamlRoot;
            }
            set => _xamlRoot = value;
        }
        public DispatcherQueue DispatcherQueue
        {
            get
            {
                if (_uiThread != 0 && Environment.CurrentManagedThreadId != _uiThread)
                {
                    WrongThreadReads++;
                    throw new InvalidOperationException("RPC_E_WRONG_THREAD: DispatcherQueue accessed off UI thread");
                }
                return _dispatcher;
            }
        }
        public double Width { get; set; }
        public double MaxHeight { get; set; }
        public bool IsEnabled { get; set; } = true;
        public Visibility Visibility { get; set; } = Visibility.Visible;
        public event Action<object, RoutedEventArgs>? Loaded { add { } remove { } }
        public event Action<object, RoutedEventArgs>? Unloaded;
        internal void RaiseUnloaded() => Unloaded?.Invoke(this, new());
        public bool Focus(FocusState state) => true;
    }
    public sealed class DispatcherQueue
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        // In-memory dispatcher is the terminal UI queue boundary, with no JoinableTask or native API.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD001", Justification = "Anonymous SynchronizationContext queue only")]
        public bool TryEnqueue(Action action)
        {
            if (_context is null) action();
            else _context.Post(_ => action(), null);
            return true;
        }
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
    public sealed class Page : FrameworkElement;
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Microsoft.UI.Xaml.Media
{
    public static class VisualTreeHelper
    {
        public static FrameworkElement? GetParent(FrameworkElement owner) => owner.Parent;
    }
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
