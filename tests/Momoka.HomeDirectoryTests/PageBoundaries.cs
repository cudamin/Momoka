using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Services;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public class FrameworkElement { public Visibility Visibility { get; set; } = Visibility.Visible; }
}
namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public sealed class Frame
    {
        public object? Content { get; set; }
        public List<object> BackStack { get; } = [];
        public List<object> ForwardStack { get; } = [];
        public int Navigations { get; private set; }
        public object? LastParameter { get; private set; }
        public void Navigate(Type page, object parameter, object? transition)
        {
            Navigations++;
            LastParameter = parameter;
            if (Content is not null) BackStack.Add(Content);
            Content = page == typeof(Momoka.Shell.Views.HomePage) ? new Momoka.Shell.Views.HomePage((Momoka.Shell.Views.HomeRequest)parameter) : new object();
        }
    }
}
namespace Momoka.Shell.Windowing { internal sealed class HostWindow; }
namespace Momoka.Shell.Views
{
    internal interface IShellContent { void Release(); }
    internal sealed class HomePage(HomeRequest request) : IShellContent
    {
        public HomeRequest Request { get; } = request;
        public void Release() { }
    }
    internal sealed class LibraryPage;
    internal sealed class MoviePilotSubscriptionsPage;
    internal sealed record MoviePilotSubscriptionsRequest(MoviePilotService Service, string Key, Action<MoviePilotSubscription> Open);
    internal sealed class LibraryRequest
    {
        public const string SearchTag = "search";
        public const string FavoritesTag = "favorites";
        public IServiceProvider Services { get; init; } = null!;
        public string Title { get; init; } = "";
        public string Tag { get; init; } = "";
        public string? ParentId { get; init; }
        public EmbySessionScope? Source { get; init; }
        public string? CollectionType { get; init; }
        public static LibraryRequest Favorites(IServiceProvider services) => new() { Services = services, Title = "favorites", Tag = FavoritesTag };
    }
    internal sealed class FavoritesBoundary
    {
        public void Attach(EmbySession session) { }
        public Task RefreshAsync() => Task.CompletedTask;
    }
    internal sealed class SignInBoundary : Microsoft.UI.Xaml.FrameworkElement { public void Detach() { } }
    internal sealed partial class ShellPage
    {
        private const string Category = "fixture-shell";
        private static readonly Dictionary<string, string> Hosted = [];
        private readonly LoadGeneration _identityLoads = new();
        private readonly Dictionary<string, LibraryRequest> _libraries = [];
        private readonly List<EmbyItem> _libraryViews = [];
        private readonly List<object> _backTrails = [];
        private readonly List<object> _forwardTrails = [];
        private readonly List<Crumb> _trail = [];
        private string? _current;
        private bool _librariesFailed;
        private string _displayedSources = "";
        private readonly EmbySession? _session;
        private readonly IServerCapabilities? _capabilities;
        private readonly IServiceProvider? _services;
        private readonly Windowing.HostWindow? _window = null;
        private readonly MoviePilotService? _moviePilotService = null;
        private FavoritesBoundary Favorites { get; } = new();
        private SignInBoundary SignIn { get; } = new();
        private Microsoft.UI.Xaml.FrameworkElement ContentHost { get; } = new();
        private Microsoft.UI.Xaml.FrameworkElement AccountButton { get; } = new();
        internal Microsoft.UI.Xaml.Controls.Frame ContentFrame { get; } = new();
        internal IReadOnlyList<EmbyItem> Directory => _libraryViews;
        internal List<string> Notices { get; } = [];
        private AppSettings Settings { get; }
        private sealed record Crumb(string Label, string Tag);

        internal ShellPage(EmbySession session, AppSettings settings)
        {
            _session = session;
            _capabilities = new CapabilitiesBoundary();
            _services = new ServicesBoundary();
            Settings = settings;
        }
        internal Task Enter() => EnterShellAsync();
        internal Task Retry() => RetryLibrariesAsync();
        internal IReadOnlyList<EmbyItem> HomeSnapshot => (ContentFrame.Content as HomePage)?.Request.LibraryViews ?? [];
        internal void LeaveForOtherPage()
        {
            _current = "different-page";
            ContentFrame.Content = new object();
        }
        internal void InvalidateVisit() => _identityLoads.Cancel();
        private string SourcesStamp() => string.Join(',', _session!.Sources.Select(source => source.Key));
        private void RebuildSidebarLibraries() { }
        private void SyncSidebar() { }
        private void ShowAccount(string? username, string? server) { }
        private void FillSwitchMenus() { }
        private void RememberTrail() { }
        private void SyncChrome() { }
        private static object? BrowseTransition() => null;
        private void Notify(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity) => Notices.Add(message);
        private void OpenSubscription(MoviePilotSubscription subscription) => throw new Momoka.Tests.AssertionException("夹具不打开订阅");
        private void ShowSettings(string category) => throw new Momoka.Tests.AssertionException("夹具不打开设置");
        private void OpenSearch() => throw new Momoka.Tests.AssertionException("夹具不打开搜索");
        private sealed class CapabilitiesBoundary : IServerCapabilities
        {
            public Version? ServerVersion => null;
            public bool? IsAdministrator => false;
            public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;
        }
        private sealed class ServicesBoundary : IServiceProvider { public object? GetService(Type serviceType) => throw new Momoka.Tests.AssertionException("夹具不创建产品服务"); }
    }
}
