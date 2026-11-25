using System.Collections.Generic;
using Momoka.Configuration;
using Momoka.LocalMedia;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public class RoutedEventArgs : EventArgs;
    public class UIElement
    {
        public Visibility Visibility { get; set; }
        public double Opacity { get; set; }
        public bool IsHitTestVisible { get; set; }
    }
    public class FrameworkElement : UIElement
    {
        public object? Tag { get; set; }
        public object? FindName(string name) => null;
    }
}

namespace Microsoft.UI.Xaml.Input
{
    public class PointerRoutedEventArgs : Xaml.RoutedEventArgs;
    public class TappedRoutedEventArgs : Xaml.RoutedEventArgs
    {
        public bool Handled { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public sealed class AppBarButton : Xaml.FrameworkElement;
    public sealed class ItemsView : Xaml.FrameworkElement;
    public sealed class ItemsViewItemInvokedEventArgs
    {
        public object? InvokedItem { get; set; }
    }
    public sealed class MenuFlyout
    {
        public IList<object> Items { get; } = new List<object>();
        public void ShowAt(Xaml.FrameworkElement target) { }
    }
    public sealed class MenuFlyoutItem : Xaml.FrameworkElement
    {
        public string Text { get; set; } = "";
        public object? Icon { get; set; }
        public event Xaml.RoutedEventHandler? Click;
        internal void RaiseClick() => Click?.Invoke(this, new Xaml.RoutedEventArgs());
    }
    public sealed class MenuFlyoutSeparator : Xaml.FrameworkElement;
    public sealed class FontIcon : Xaml.FrameworkElement
    {
        public string Glyph { get; set; } = "";
    }
    public class Page : Xaml.FrameworkElement
    {
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        protected virtual void OnNavigatedTo(Xaml.Navigation.NavigationEventArgs args) { }
        protected virtual void OnNavigatedFrom(Xaml.Navigation.NavigationEventArgs args) { }
    }
}

namespace Microsoft.UI.Xaml.Navigation
{
    public sealed class NavigationEventArgs(object? parameter = null) : EventArgs
    {
        public object? Parameter { get; } = parameter;
    }
}

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public bool TryEnqueue(Action action) { action(); return true; }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class LocalLibraryPage
    {
        private void InitializeComponent() { }
        internal void InvokeRefresh() => OnRefresh(new Microsoft.UI.Xaml.Controls.AppBarButton(), new());
        internal void InvokeRetry() => OnRetryIncomplete(new Microsoft.UI.Xaml.Controls.AppBarButton(), new());
        internal void InvokeOpenSource() => OnOpenSource(new Microsoft.UI.Xaml.Controls.AppBarButton(), new());
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage;
}

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static TaskCompletionSource<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>? Pending { get; set; }
        public static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int logicalWidth)
            => Pending?.Task ?? Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(null);
    }
}

namespace Momoka.Shell.Platform
{
    internal interface ISystemLauncher
    {
        void OpenFolder(string path);
        void OpenUrl(string url);
    }
}

namespace Momoka.Shell.Services
{
    internal sealed class LocalLibraryCoordinator
    {
        internal List<LocalMediaLibrarySettings> Configurations { get; } = [];
        internal List<LocalMediaLibraryEntry> Entries { get; } = [];
        public IReadOnlyList<LocalMediaLibrarySettings> Libraries => Configurations;
        public IReadOnlyList<LocalMediaLibraryEntry> EntriesFor(string id) => Entries.Where(entry => entry.LibraryId == id).ToArray();
        public event EventHandler? Changed;
        public string StatusFor(string id) => "后台监控已开启";
        internal Func<string, Task>? Refresh { get; set; }
        internal Func<string, Task>? RetryIncomplete { get; set; }
        internal Func<string, string, bool?, bool?, bool>? SetUserState { get; set; }
        public Task RefreshAsync(string id) => Refresh?.Invoke(id) ?? Task.CompletedTask;
        public Task RetryIncompleteAsync(string id) => RetryIncomplete?.Invoke(id) ?? Task.CompletedTask;
        public bool SetLocalUserState(string libraryId, string groupKey, bool? watched, bool? favorite) =>
            SetUserState?.Invoke(libraryId, groupKey, watched, favorite) ?? true;
        internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        internal int Subscribers => Changed?.GetInvocationList().Length ?? 0;
    }
}
