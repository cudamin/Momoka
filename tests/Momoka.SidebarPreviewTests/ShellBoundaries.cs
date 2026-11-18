using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Interop;
using Microsoft.UI.Input;
using Windows.Foundation;

namespace Momoka.Shell.Views
{
    internal static class PreviewTree
    {
        internal static IEnumerable<UIElement> Descendants(UIElement root)
        {
            IEnumerable<UIElement> children = root switch
            {
                Grid grid => grid.Children,
                StackPanel panel => panel.Children,
                Border { Child: { } child } => [child],
                Button { Content: UIElement content } => [content],
                _ => []
            };
            foreach (var child in children)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }

    internal static class PreviewClock
    {
        internal static long Now;
    }

    internal sealed record LibraryRequest(string Title, string ParentId, string CollectionType);
    internal static class HomePage { internal const double ChromeHeight = 48; }
    internal static class HomeMotion { internal static bool AnimationsEnabled => false; }

    /// <summary>Only the surrounding shell/UI endpoints are substitutes; its complete preview partial is production source.</summary>
    public sealed partial class ShellPage : FrameworkElement
    {
        private readonly AppSettings? _settings;
        private readonly EmbySession? _session;
        private readonly IServiceProvider? _services;
        private readonly Dictionary<string, LibraryRequest> _libraries = [];
        private readonly List<EmbyItem> _libraryViews = [];
        internal AppSettings Settings => _settings!;
        internal bool SidebarShown { get; set; } = true;
        internal NavigationView Sidebar { get; } = new();
        internal FrameworkElement SidebarHost { get; } = new() { ActualWidth = 56 };
        // 订阅入口：接线与预览面板与库项共用，这里只给两项让浮层认得出 Tag 与标题。
        internal NavigationViewItem SidebarMoviePilotMovies { get; } = new()
            { Content = "订阅·电影", Tag = HomeLayout.MoviePilotMovies, XamlRoot = new() };
        internal NavigationViewItem SidebarMoviePilotSeries { get; } = new()
            { Content = "订阅·电视剧", Tag = HomeLayout.MoviePilotSeries, XamlRoot = new() };
        private readonly MoviePilotService? _moviePilotService = null;
        internal List<MoviePilotSubscription> OpenedSubscriptions { get; } = [];
        private void OpenSubscription(MoviePilotSubscription subscription) => OpenedSubscriptions.Add(subscription);
        internal Border LibraryPreviewPanel { get; } = new() { Width = 360, Visibility = Visibility.Collapsed };
        internal Grid LibraryPreviewCovers { get; } = new();
        internal TextBlock LibraryPreviewTitle { get; } = new();
        internal TextBlock LibraryPreviewMeta { get; } = new();
        internal TextBlock LibraryPreviewEmpty { get; } = new();
        internal ProgressRing LibraryPreviewLoading { get; } = new();
        internal Button LibraryPreviewMore { get; } = new();
        internal DispatcherQueue DispatcherQueue { get; } = new();
        internal List<EmbyItem> Played { get; } = [];
        internal List<string> Navigated { get; } = [];
        internal bool PreviewWasClosedOnPlay { get; private set; }
        internal bool Visible => LibraryPreviewPanel.Visibility == Visibility.Visible;
        internal string? DisplayedTag => _previewTag;
        internal NavigationViewItem? DisplayedAnchor => _previewAnchor;
        internal NavigationViewItem? HoverAnchor => _previewHoverAnchor;
        internal DispatcherQueueTimer OpenTimer => _previewOpenTimer!;
        internal DispatcherQueueTimer CloseTimer => _previewCloseTimer!;
        internal CancellationTokenSource? CurrentLoad => _previewLoad;

        internal ShellPage(AppSettings settings, EmbySession session, IServiceProvider? services = null)
        {
            _settings = settings;
            _session = session;
            _services = services;
            ActualWidth = 760;
            ActualHeight = 650;
            LibraryPreviewPanel.XamlRoot = XamlRoot;
            Sidebar.XamlRoot = XamlRoot;
            PreviewClock.Now = 0;
            Native.PointerAvailable = true;
            InitializeLibraryPreview();
        }

        internal NavigationViewItem AddLibrary(EmbyItem library, double y)
        {
            _libraryViews.Add(library);
            var tag = HomeLayout.LibraryKey(library);
            _libraries.Add(tag, new LibraryRequest(library.Name, library.Id, library.CollectionType ?? "movies"));
            var anchor = new NavigationViewItem
            {
                Tag = tag,
                Content = library.Name,
                Origin = new Point(0, y),
                ActualWidth = 56,
                ActualHeight = 44,
                XamlRoot = XamlRoot
            };
            _previewDwells.Add(anchor, new PosterHoverDwell());
            return anchor;
        }

        internal void At(NavigationViewItem item, long now, int x = 20, int offsetY = 20)
        {
            PreviewClock.Now = now;
            Native.PointerPosition = new NativePoint { X = x, Y = (int)item.Origin.Y + offsetY };
        }

        internal void Enter(NavigationViewItem item, PointerDeviceType type = PointerDeviceType.Mouse, bool pressed = false)
            => OnSidebarLibraryPointerEntered(item, Event(type, pressed));
        internal void Move(NavigationViewItem item, PointerDeviceType type = PointerDeviceType.Mouse, bool pressed = false)
            => OnSidebarLibraryPointerMoved(item, Event(type, pressed));
        internal void Exit(NavigationViewItem item) => OnSidebarLibraryPointerExited(item, Event());
        internal void PressSidebar() => Sidebar.Press(Event(pressed: true));
        internal void InvokeNavigation(NavigationViewItem item) => SuppressLibraryPreview(item);
        internal bool Suppressed(NavigationViewItem item) => _previewDwells[item].IsSuppressed;
        internal Task Show(NavigationViewItem item) => ShowLibraryPreviewAsync(item, (string)item.Tag!);
        internal void Hide() => HideLibraryPreview();
        internal void More() => OnLibraryPreviewMoreClicked(LibraryPreviewMore, new());
        internal void EnterPanel() => OnLibraryPreviewPointerEntered(LibraryPreviewPanel, Event());
        internal void ExitPanel() => OnLibraryPreviewPointerExited(LibraryPreviewPanel, Event());
        internal void RebuildBoundary()
        {
            HideLibraryPreview();
            _previewDwells.Clear();
            _libraries.Clear();
            _libraryViews.Clear();
        }
        internal Task LoadImage(Image picture, FontIcon placeholder, EmbyItem item, EmbySessionScope scope, CancellationTokenSource load, EmbyImageStore images)
        {
            _previewImages = images;
            return LoadPreviewCoverAsync(picture, placeholder, item, scope, EmbyImageStore.Primary, 100, load);
        }
        internal bool IsCurrent(CancellationTokenSource load) => IsCurrentPreview(load);
        internal void ReplaceLoadBoundary(CancellationTokenSource load) => _previewLoad = load;
        internal void ClickHandler(Button button, EmbyItem item, CancellationTokenSource load) => OnLibraryPreviewCoverClicked(button, item, load);
        internal (FrameworkElement Frame, Image Picture, FontIcon Placeholder) Create(EmbyItem item, CancellationTokenSource load)
        {
            var (cell, picture, placeholder) = CreatePreviewCover(100, 0, item, load);
            return (((StackPanel)cell).Children.OfType<FrameworkElement>().First(), picture, placeholder);
        }
        private Task PlayAsync(EmbyItem item)
        {
            PreviewWasClosedOnPlay = !Visible && LibraryPreviewCovers.Children.Count == 0;
            Played.Add(item);
            return Task.CompletedTask;
        }
        private void GoTo(string tag) => Navigated.Add(tag);
        private void SyncSidebar() { }
        private static PointerRoutedEventArgs Event(PointerDeviceType type = PointerDeviceType.Mouse, bool pressed = false)
        {
            var args = new PointerRoutedEventArgs();
            args.Pointer.PointerDeviceType = type;
            args.Point.Properties.IsLeftButtonPressed = pressed;
            return args;
        }
    }
}

namespace Momoka.Shell.Media
{
    internal sealed class Bitmap;
    internal static class PosterLoader
    {
        internal static Func<byte[], int, Task<Bitmap?>> Decode { get; set; } = (_, _) => Task.FromResult<Bitmap?>(new());
        internal static Task<Bitmap?> DecodeAsync(byte[] bytes, int width) => Decode(bytes, width);
    }
}
