// Inert WinUI/XAML/value boundaries. No sockets, real windows, account IO or playback.
// Complete production view models, views and extracted command bodies execute against these values.
namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
}
namespace Windows.System { public enum VirtualKey { Enter, Escape, Back, Control, Menu, LeftWindows, RightWindows } }
namespace Windows.UI.Core { public enum CoreVirtualKeyStates { None, Down } }
namespace Microsoft.UI.Input
{
    public static class InputKeyboardSource
    {
        public static HashSet<Windows.System.VirtualKey> KeysDown { get; } = [];
        public static Windows.UI.Core.CoreVirtualKeyStates GetKeyStateForCurrentThread(Windows.System.VirtualKey key) =>
            KeysDown.Contains(key) ? Windows.UI.Core.CoreVirtualKeyStates.Down : Windows.UI.Core.CoreVirtualKeyStates.None;
    }
}
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public static System.Collections.Concurrent.ConcurrentQueue<Action> Pending { get; } = new();
        public bool TryEnqueue(Action action) { Pending.Enqueue(action); return true; }
        public static void Drain() { while (Pending.TryDequeue(out var action)) action(); }
    }
}
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public sealed class XamlRoot;
    public class RoutedEventArgs : EventArgs { public object? OriginalSource { get; init; } }
    public sealed class ExceptionRoutedEventArgs : RoutedEventArgs;
    public delegate void ExceptionRoutedEventHandler(object sender, ExceptionRoutedEventArgs args);
    public class FrameworkElement
    {
        public object? Tag { get; set; }
        public Visibility Visibility { get; set; }
        public bool IsHitTestVisible { get; set; } = true;
        public XamlRoot? XamlRoot { get; set; } = new();
        public ElementTheme RequestedTheme { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    }
}
namespace Microsoft.UI.Xaml.Input
{
    public sealed class KeyRoutedEventArgs : EventArgs
    {
        public Windows.System.VirtualKey Key { get; set; }
        public bool Handled { get; set; }
        public object? OriginalSource { get; set; }
    }
}
namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage(Uri uri)
    {
        public Uri UriSource { get; } = uri;
        public int DecodePixelWidth { get; set; }
        public byte[] Payload { get; } = new byte[32 * 1024];
        public event Microsoft.UI.Xaml.ExceptionRoutedEventHandler? ImageFailed;
        public int Subscribers => ImageFailed?.GetInvocationList().Length ?? 0;
        public void Fail() => ImageFailed?.Invoke(this, new());
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public enum ContentDialogResult { None, Primary, Secondary }
    public sealed class ContentDialogOpenedEventArgs : EventArgs;
    public sealed class ContentDialogClosedEventArgs : EventArgs;
    public sealed class ContentDialogButtonClickEventArgs : EventArgs { public bool Cancel { get; set; } }
    public sealed class SelectionChangedEventArgs : EventArgs;
    public class UserControl : Microsoft.UI.Xaml.FrameworkElement;
    public class ItemsView : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? ItemsSource { get; set; }
        public ScrollView? ScrollView { get; set; } = new();
        public void UpdateLayout() { }
    }
    public enum ScrollingAnimationMode { Disabled }
    public sealed class ScrollingScrollOptions(ScrollingAnimationMode mode) { public ScrollingAnimationMode Mode { get; } = mode; }
    public sealed class ScrollView
    {
        public double VerticalOffset { get; set; }
        public void ScrollTo(double horizontal, double vertical, ScrollingScrollOptions options) => VerticalOffset = vertical;
    }
    public sealed class ItemsViewItemInvokedEventArgs : EventArgs { public object? InvokedItem { get; init; } }
    public sealed class TextBox : Microsoft.UI.Xaml.FrameworkElement
    {
        public string Name { get; init; } = "TextBox";
        public string Text { get; set; } = "";
        public string PlaceholderText { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }
        public string SelectedText
        {
            get => Text.Substring(SelectionStart, SelectionLength);
            set
            {
                Text = Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, value);
                Select(SelectionStart + value.Length, 0);
            }
        }
        public void Select(int start, int length) => (SelectionStart, SelectionLength) = (start, length);
        public event Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? PreviewKeyDown;
        public event Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? KeyUp;
        internal void Press(Windows.System.VirtualKey key) => PreviewKeyDown?.Invoke(this, new() { Key = key });
        internal bool Release(Windows.System.VirtualKey key, object? originalSource = null)
        {
            var args = new Microsoft.UI.Xaml.Input.KeyRoutedEventArgs { Key = key, OriginalSource = originalSource ?? this };
            KeyUp?.Invoke(this, args);
            return args.Handled;
        }
        public event Action<TextBox, TextCompositionStartedEventArgs>? TextCompositionStarted;
        public event Action<TextBox, TextCompositionEndedEventArgs>? TextCompositionEnded;
        public event Action<object, Microsoft.UI.Xaml.RoutedEventArgs>? LostFocus;
        internal void BeginComposition() => TextCompositionStarted?.Invoke(this, new());
        internal void EndComposition() => TextCompositionEnded?.Invoke(this, new());
        internal void Blur() => LostFocus?.Invoke(this, new());
    }
    public sealed class TextCompositionStartedEventArgs : EventArgs;
    public sealed class TextCompositionEndedEventArgs : EventArgs;
    public sealed class AutoSuggestBox : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; }
    public sealed class AutoSuggestBoxQuerySubmittedEventArgs : EventArgs { public string? QueryText { get; init; } }
    public enum AutoSuggestionBoxTextChangeReason { UserInput, ProgrammaticChange }
    public sealed class AutoSuggestBoxTextChangedEventArgs : EventArgs
    {
        public AutoSuggestionBoxTextChangeReason Reason { get; init; }
        public bool Current { get; init; } = true;
        public bool CheckCurrent() => Current;
    }
    public sealed class ComboBox : Microsoft.UI.Xaml.FrameworkElement { public int SelectedIndex { get; set; } }
    public sealed class Button : Microsoft.UI.Xaml.FrameworkElement { public bool IsEnabled { get; set; } = true; }
    public sealed class ProgressBar : Microsoft.UI.Xaml.FrameworkElement;
    public sealed class InfoBar : Microsoft.UI.Xaml.FrameworkElement
    {
        public string Message { get; set; } = "";
        public bool IsOpen { get; set; }
    }
    public sealed class ListView : Microsoft.UI.Xaml.FrameworkElement
    {
        private object? _itemsSource;
        public object? ItemsSource
        {
            get => _itemsSource;
            set
            {
                _itemsSource = value;
                SelectedItem = null;
                SelectionChanged?.Invoke(this, new());
                ItemsSourceChanged?.Invoke(value);
            }
        }
        public Action<object?>? ItemsSourceChanged { get; set; }
        public object? SelectedItem { get; set; }
        public event Action<object, SelectionChangedEventArgs>? SelectionChanged;
        public void Select(object? item) { SelectedItem = item; SelectionChanged?.Invoke(this, new()); }
    }
    public class ContentDialog : Microsoft.UI.Xaml.FrameworkElement
    {
        public static Func<ContentDialog, Task<ContentDialogResult>>? ShowEndpoint { get; set; }
        public event Action<ContentDialog, ContentDialogOpenedEventArgs>? Opened;
        public event Action<ContentDialog, ContentDialogClosedEventArgs>? Closed;
        public bool IsPrimaryButtonEnabled { get; set; }
        public void OpenFixture() => Opened?.Invoke(this, new());
        public void CloseFixture() => Closed?.Invoke(this, new());
        public async Task<ContentDialogResult> ShowAsync()
        {
            OpenFixture();
            var result = await (ShowEndpoint?.Invoke(this) ?? Task.FromResult(ContentDialogResult.None));
            CloseFixture();
            return result;
        }
    }
}
namespace Momoka.Shell.Platform
{
    public interface ISystemLauncher;

    /// <summary>系统通知是平台能力；这个夹具只跑页面控制流，通知本身不落地、也不证明真的弹过。</summary>
    public static class SystemNotification
    {
        public static int Shown { get; private set; }
        public static void Show(string title, string message) { Shown++; }
    }
}
namespace Momoka.Shell.Views
{
    public static class ThemeHost
    {
        public sealed record Theme(bool IsDark);
        public static Theme Current { get; } = new(true);
    }
    public sealed class MoviePilotResourcePanel : Microsoft.UI.Xaml.FrameworkElement
    {
        public int Opens { get; private set; }
        public int Releases { get; private set; }
        public void Open(Momoka.MoviePilot.MoviePilotService service, Momoka.MoviePilot.MoviePilotMedia media,
            Momoka.Shell.Platform.ISystemLauncher launcher)
        { Opens++; }
        public void Release() { Releases++; }
    }
    public static class MoviePilotSeasonPicker
    {
        public static int Opens { get; private set; }
        internal static Task<Momoka.Shell.ViewModels.MoviePilotSubscribeReport> ShowAsync(Microsoft.UI.Xaml.FrameworkElement owner, Momoka.MoviePilot.MoviePilotService service,
            Momoka.MoviePilot.MoviePilotMedia media, string group, int? season, CancellationToken token)
        { Opens++; return Task.FromResult(Momoka.Shell.ViewModels.MoviePilotSubscribeReport.Cancelled); }
        internal static Task<Momoka.Shell.ViewModels.MoviePilotSubscribeReport> ShowAsync(Microsoft.UI.Xaml.FrameworkElement owner, SubscriptionPageFixtureService service,
            Momoka.MoviePilot.MoviePilotMedia media, string group, int? season, CancellationToken token) => service.Pick(token);
    }
    public sealed partial class MoviePilotSearchPanel
    {
        internal MoviePilotResourcePanel ResourceOverlay { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement SearchContent { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ItemsView Results { get; } = new();
        private void InitializeComponent() { Results.ItemsSource = ViewModel.Results; }
    }
    public sealed partial class FnNasMatchDialog
    {
        internal Microsoft.UI.Xaml.Controls.TextBox KeywordBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox SourceBox { get; } = new();
        internal Microsoft.UI.Xaml.Controls.Button SearchButton { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ProgressBar Busy { get; } = new();
        internal Microsoft.UI.Xaml.Controls.InfoBar Problem { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ListView Results { get; } = new();
        private void InitializeComponent() { Results.SelectionChanged += OnSelectionChanged; }
        internal Task SubmitFixture() => SearchAsync();
        internal Microsoft.UI.Xaml.Controls.ContentDialogButtonClickEventArgs MatchFixture()
        { var args = new Microsoft.UI.Xaml.Controls.ContentDialogButtonClickEventArgs(); OnMatch(this, args); return args; }
    }
    // The command only consumes the current item and not the actual card rendering/image lifecycle.
    public sealed class CardItem(Momoka.Emby.EmbyItem item)
    {
        public Momoka.Emby.EmbyItem Item { get; } = item;
        public bool IsCurrent => Item.Source?.IsAvailable != false;
        // This search fixture has no continue-watching row; that unrelated action must never dispatch.
        public bool AllowResumeRemoval => false;
    }
    internal static class ConfirmDialog
    {
        internal static Func<string, string, string, Task<bool>> ForLibrary(Microsoft.UI.Xaml.FrameworkElement owner) =>
            (_, _, _) => throw new InvalidOperationException("搜索夹具禁止续播移除确认");
    }
    public interface IShellActions
    {
        void Notify(string text, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        Momoka.Emby.ItemMenuPermissions MenuPermissionsFor(Momoka.Emby.EmbySessionScope scope);
    }
    internal static partial class ItemCommands
    {
        internal static Task MatchFixture(Momoka.Emby.EmbySessionScope session, IShellActions shell,
            Microsoft.UI.Xaml.FrameworkElement owner, CardItem card, Action? changed = null) =>
            FnNasMatchAsync(session, shell, owner, card, changed);
    }
}
