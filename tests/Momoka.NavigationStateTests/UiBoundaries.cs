using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;

// Only platform/page-model boundaries are simulated. The extracted page visits and shell
// navigation methods execute unchanged, without a HWND, saved account or media request.
namespace Microsoft.UI.Xaml
{
    public class FrameworkElement { public object? Tag { get; set; } }
    public sealed class RoutedEventArgs : EventArgs;
}
namespace Microsoft.UI.Xaml.Navigation
{
    public enum NavigationMode { New, Back, Forward }
    public sealed class NavigationEventArgs(object? parameter, NavigationMode mode = NavigationMode.New) : EventArgs
    {
        public object? Parameter { get; } = parameter;
        public NavigationMode NavigationMode { get; } = mode;
    }
}
namespace Microsoft.UI.Xaml.Media.Animation
{
    public abstract class NavigationTransitionInfo;
    public sealed class SuppressNavigationTransitionInfo : NavigationTransitionInfo;
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Page : FrameworkElement
    {
        protected virtual void OnNavigatedTo(NavigationEventArgs args) { }
        protected virtual void OnNavigatedFrom(NavigationEventArgs args) { }
        internal void VisitForTest(object? request, NavigationMode mode = NavigationMode.New) => OnNavigatedTo(new(request, mode));
        internal void LeaveForTest(NavigationMode mode = NavigationMode.New) => OnNavigatedFrom(new(null, mode));
    }
    public sealed record EntryBoundary(Page Page, object Request);    // This models only the Frame's synchronous page hand-off and two history stacks.
    // It does not assert native Frame caching, rendering, input or event ordering.
    public sealed class Frame
    {
        private EntryBoundary? _current;
        public Page? Content => _current?.Page;
        public List<EntryBoundary> BackStack { get; } = [];
        public List<EntryBoundary> ForwardStack { get; } = [];
        public bool CanGoBack => BackStack.Count > 0;
        public bool CanGoForward => ForwardStack.Count > 0;
        public int Navigations { get; private set; }
        public int BackNavigations { get; private set; }
        public int ForwardNavigations { get; private set; }
        public bool Navigate(Type pageType, object request, object? transition = null)
        {
            if (_current is not null) { _current.Page.LeaveForTest(); BackStack.Add(_current); }
            ForwardStack.Clear();
            var page = (Page)Activator.CreateInstance(pageType)!;
            _current = new(page, request);
            page.VisitForTest(request);
            Navigations++;
            return true;
        }
        public void GoBack(object? transition = null)
        {
            if (_current is null || !CanGoBack) throw new InvalidOperationException("No backward frame entry");
            _current.Page.LeaveForTest(NavigationMode.Back);
            ForwardStack.Add(_current);
            _current = BackStack[^1]; BackStack.RemoveAt(BackStack.Count - 1);
            _current.Page.VisitForTest(_current.Request, NavigationMode.Back);
            BackNavigations++;
        }
        public void GoForward()
        {
            if (_current is null || !CanGoForward) throw new InvalidOperationException("No forward frame entry");
            _current.Page.LeaveForTest(NavigationMode.Forward);
            BackStack.Add(_current);
            _current = ForwardStack[^1]; ForwardStack.RemoveAt(ForwardStack.Count - 1);
            _current.Page.VisitForTest(_current.Request, NavigationMode.Forward);
            ForwardNavigations++;
        }
    }
}
namespace Microsoft.Extensions.DependencyInjection
{
    internal static class BoundaryServiceExtensions
    {
        internal static T GetRequiredService<T>(this IServiceProvider services) =>
            services.GetService(typeof(T)) is T result ? result : throw new InvalidOperationException("Missing boundary service " + typeof(T).Name);
    }
}
namespace Momoka.Shell.ViewModels
{
    public sealed class MoviePilotSearchSnapshot;
    public sealed class MusicPlaybackViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string CurrentKey => "";
        public void RaiseForTest() => PropertyChanged?.Invoke(this, new(nameof(CurrentKey)));
    }
    public sealed class MusicLibraryViewModel
    {
        public int Attaches { get; private set; }
        public int Reloads { get; private set; }
        public int Releases { get; private set; }
        internal void Attach(Views.LibraryRequest request, EmbySession session, EmbyImageStore images) => Attaches++;
        internal void MarkPlaying(string key) { }
        internal Task ReloadAsync() { Reloads++; return Task.CompletedTask; }
        internal void Release() => Releases++;
    }
}
namespace Momoka.Shell.Platform
{
    internal interface ISystemLauncher;
}
namespace Momoka.Shell.Windowing
{
    internal sealed class HostWindow;
}
namespace Momoka.Shell.Views
{
    internal interface IShellActions { void SetTitleStrip(TitleStrip strip); }
    internal readonly record struct TitleStrip { internal static TitleStrip Plain => new(); }
    public sealed partial class MusicLibraryPage
    {
        private IShellActions? _actions;
        private LibraryRequest? _request;
        private ViewModels.MusicPlaybackViewModel? _music;
        private bool _released;
        public ViewModels.MusicLibraryViewModel ViewModel { get; } = new();
        internal object? RequestForTest => _request;
        internal bool ReleasedForTest => _released;
        internal int ArtworkDetachesForTest { get; private set; }
        internal int TrackArtworkStopsForTest { get; private set; }
        private FrameworkElement MusicRows { get; } = new();
        private void DetachWallArtwork() => ArtworkDetachesForTest++;
        private void DetachSearchInput() { }
        private void SetArtworkRealized(FrameworkElement root, bool realized)
        {
            if (!ReferenceEquals(root, MusicRows) || realized)
                throw new InvalidOperationException("导航释放边界仅允许结束曲目图片需求");
            TrackArtworkStopsForTest++;
        }
        private void PlaceSearchBox() { }
        private void OnMusicChanged(object? sender, PropertyChangedEventArgs args) { }
    }
    internal sealed record HomeRequest(IServiceProvider Services, IReadOnlyList<EmbyItem> LibraryViews,
        Windowing.HostWindow? Window, Action<MoviePilotSubscription> Open)
    {
        internal Action<string>? OpenSubscriptionLibrary { get; init; }
        internal Func<IReadOnlyList<EmbyItem>>? CurrentLibraryViews { get; init; }
        internal Action<EmbySessionScope, IReadOnlyList<EmbyItem>>? LibraryDirectoryUpdated { get; init; }
    }
    public sealed class HomePage : Page
    {
        protected override void OnNavigatedTo(NavigationEventArgs args) => Tag = "home";
    }
    public sealed class LibraryPage : Page
    {
        protected override void OnNavigatedTo(NavigationEventArgs args) => Tag = (args.Parameter as LibraryRequest)?.Tag;
    }
    public sealed class MoviePilotSubscriptionsPage : Page;
    internal sealed class MoviePilotCalendarPage : Page
    {
        internal const string Destination = "moviepilot:calendar";
    }
    internal sealed record MoviePilotCalendarRequest(MoviePilotService? Service, MoviePilotSettings Settings, Momoka.AniBt.AniBtClient AniBt);
    internal sealed record MoviePilotSubscriptionsRequest(MoviePilotService Service, string Key, Action<MoviePilotSubscription> Open)
    {
        internal const string LibraryKey = "mp-subscriptions";
        internal string Title => Key;
    }
    internal sealed class MenuBoundary { internal int Hides { get; private set; } internal void Hide() => Hides++; }
    internal sealed partial class ShellPage
    {
        private const string Category = "anonymous-navigation-fixture";
        private readonly IServiceProvider _services;
        private readonly EmbySession _session;
        private readonly Dictionary<string, LibraryRequest> _libraries = [];
        private readonly List<EmbyItem> _libraryViews = [];
        private static readonly Dictionary<string, string> Hosted = [];
        private readonly List<Crumb[]> _backTrails = [];
        private readonly List<Crumb[]> _forwardTrails = [];
        private readonly ObservableCollection<Crumb> _trail = [];
        private readonly Windowing.HostWindow? _window = null;
        private readonly MoviePilotService? _moviePilotService = null;
        private bool CalendarAvailable => false;
        private AppSettings Settings { get; } = new();
        private bool _closing;
        private string? _current;
        private readonly MenuBoundary AccountMenu = new();
        internal Frame ContentFrame { get; } = new();
        internal string? CurrentForTest => _current;
        internal ShellPage(IServiceProvider services, EmbySession session) { _services = services; _session = session; }
        internal void AddLibraryForTest(LibraryRequest request) => _libraries.Add(request.Tag, request);
        internal void BackForTest() => GoBack();
        internal void ForwardForTest() => GoForward();
        internal void AdoptForTest() => AdoptContentTag();
        internal void LeaveForHomeForTest() => GoTo("home");
        internal void ClosingForTest() => _closing = true;
        private void SyncChrome() { }
        private void PruneMoviePilotHistory() { }
        private static string SourcesStamp() => "anonymous";
        private void OpenSubscription(MoviePilotSubscription subscription) => throw new InvalidOperationException("No subscription navigation in this fixture");
        private void ApplyLibraryDirectory(EmbySessionScope source, IReadOnlyList<EmbyItem> views) => throw new InvalidOperationException("No directory reads in this fixture");
        private void ShowSettings(string category) => throw new InvalidOperationException("No settings window in this fixture");
        private void OpenSearch() => throw new InvalidOperationException("No search in this fixture");
    }
}
