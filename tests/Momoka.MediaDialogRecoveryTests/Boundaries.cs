using Momoka.Tests;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public sealed class RoutedEventArgs : EventArgs;
    public class FrameworkElement
    {
        public object? Tag { get; set; }
        public Visibility Visibility { get; set; }
        public XamlRoot? XamlRoot { get; set; }
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public Dictionary<string, object> Resources { get; } = new() { ["EgSpaceXS"] = 4d };
    }
    public sealed class XamlRoot { public IslandEnvironment ContentIslandEnvironment { get; } = new(); }
    public sealed class IslandEnvironment { public Microsoft.UI.WindowId AppWindowId { get; } = new() { Value = 1 }; }
}

namespace Microsoft.UI
{
    public struct WindowId { public ulong Value { get; set; } }
    public static class Win32Interop { public static IntPtr GetWindowFromWindowId(WindowId id) => (IntPtr)1; }
}
namespace WinRT { public static class Interop { public static void InitializeWithWindowInitialize(object picker, IntPtr handle) { } public static class InitializeWithWindow { public static void Initialize(object picker, IntPtr handle) { } } } }
namespace Windows.Storage.Pickers
{
    public sealed class FileOpenPicker
    {
        public static FakeFile? Selected { get; set; }
        public List<string> FileTypeFilter { get; } = [];
        public Task<FakeFile?> PickSingleFileAsync() => Task.FromResult(Selected);
    }
    public sealed class FakeFile(ulong size)
    {
        public string Name => "fixture.jpg";
        public int Opens { get; private set; }
        public Task<FakeProperties> GetBasicPropertiesAsync() => Task.FromResult(new FakeProperties(size));
        public Task<FakeStream> OpenReadAsync() { Opens++; return Task.FromResult(new FakeStream(size)); }
    }
    public sealed record FakeProperties(ulong Size);
    public sealed class FakeStream(ulong size) : IDisposable { public ulong Size => size; public object GetInputStreamAt(ulong offset) => new(); public void Dispose() { } }
}
namespace Windows.Storage.Streams
{
    public sealed class DataReader(object stream) : IDisposable
    {
        private readonly object _stream = stream;
        public Task<uint> LoadAsync(uint size) => Task.FromResult(size);
        public void ReadBytes(byte[] bytes) { }
        public void Dispose() { GC.KeepAlive(_stream); }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum ContentDialogResult { None, Primary, Secondary }
    public sealed class ContentDialogClosingEventArgs : EventArgs { public bool Cancel { get; set; } }
    public sealed class ContentDialogClosedEventArgs : EventArgs;
    public sealed class ContentDialogButtonClickEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
        public ContentDialogButtonClickDeferral GetDeferral() => new();
    }
    public sealed class ContentDialogButtonClickDeferral { public void Complete() { } }
    public class ContentDialog : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Title { get; set; }
        public Microsoft.UI.Xaml.ElementTheme RequestedTheme { get; set; }
        public bool IsPrimaryButtonEnabled { get; set; } = true;
        public bool IsClosed { get; private set; }
        private TaskCompletionSource<ContentDialogResult>? _shown;
        public Task<ContentDialogResult> ShowAsync() { IsClosed = false; return (_shown = new()).Task; }
        public event Action<ContentDialog, ContentDialogClosingEventArgs>? Closing;
        public event Action<ContentDialog, ContentDialogClosedEventArgs>? Closed;
        public bool TryClose()
        {
            var args = new ContentDialogClosingEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel) return false;
            IsClosed = true;
            Closed?.Invoke(this, new());
            _shown?.TrySetResult(ContentDialogResult.None);
            return true;
        }
        public void Hide() => TryClose();
    }
    public sealed class TextBox : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; public object? Header { get; set; } }
    public sealed class TextBlock : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; }
    public sealed class Button : Microsoft.UI.Xaml.FrameworkElement
    {
        public bool IsEnabled { get; set; } = true;
        internal Action? Invoke { get; set; }
    }
    public sealed class CheckBox : Microsoft.UI.Xaml.FrameworkElement { public bool? IsChecked { get; set; } }
    public sealed class InfoBar { public string Message { get; set; } = ""; public bool IsOpen { get; set; } }
    public sealed class StackPanel : Microsoft.UI.Xaml.FrameworkElement { public double Spacing { get; set; } public List<object> Children { get; } = []; }
    public sealed class HyperlinkButton { public object? Content { get; set; } public Uri? NavigateUri { get; set; } }
}

namespace Microsoft.UI.Xaml.Media.Imaging { public sealed class BitmapImage; }
namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static int Decodes;
        internal static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] data, int width)
        { Decodes++; return Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(new()); }
    }
}
namespace Momoka.Shell.Views
{
    internal static class ThemeHost { internal static Theme Current { get; } = new(); }
    internal sealed class Theme { public bool IsDark => true; }
    public sealed partial class IdentifyDialog
    {
        internal Microsoft.UI.Xaml.Controls.TextBox NameBox = new() { Text = "fixture" }, YearBox = new();
        internal Microsoft.UI.Xaml.Controls.TextBlock ChosenName = new(), ChosenDetail = new(), ConfirmText = new(), PathText = new();
        internal Microsoft.UI.Xaml.Controls.CheckBox ReplaceImages = new();
        internal Microsoft.UI.Xaml.Controls.InfoBar Problem = new();
        internal Microsoft.UI.Xaml.Controls.Button SearchButton = new(), RemoveButton = new(), ApplyButton = new(), ConfirmRemoveButton = new(), CancelRemoveButton = new(), BackToFormButton = new(), BackToResultsButton = new();
        internal Microsoft.UI.Xaml.Controls.StackPanel SearchForm = new(), ConfirmArea = new(), ResultsArea = new(), ApplyArea = new(), NoResults = new(), PathRow = new(), ProviderFields = new();
        private void InitializeComponent()
        {
            SearchButton.Invoke = () => OnSearch(this, new());
            RemoveButton.Invoke = () => OnAskRemove(this, new());
            ConfirmRemoveButton.Invoke = () => OnConfirmRemove(this, new());
            ApplyButton.Invoke = () => OnApply(this, new());
        }
        internal Microsoft.UI.Xaml.Controls.Button PickButton(IdentifyCandidate candidate) => new()
        {
            Tag = candidate,
            Visibility = Microsoft.UI.Xaml.Visibility.Visible,
            Invoke = () => OnPick(new Microsoft.UI.Xaml.FrameworkElement { Tag = candidate }, new())
        };
        internal void Apply(Momoka.Emby.RemoteSearchResult result) { _chosen = new(result); OnApply(this, new()); }
        internal void Search() => OnSearch(this, new());
        internal void Remove() => OnConfirmRemove(this, new());
    }
    public sealed partial class FnNasMetadataDialog
    {
        internal Microsoft.UI.Xaml.Controls.TextBox NameBox = new(), OverviewBox = new(), RatingBox = new(), ReleaseBox = new();
        internal Microsoft.UI.Xaml.Controls.InfoBar Problem = new();
        private void InitializeComponent() { }
        internal void Save() => OnSave(this, new());
    }
}
