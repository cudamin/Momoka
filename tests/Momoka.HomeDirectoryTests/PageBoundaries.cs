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
    internal sealed class FavoritesBoundary : IDisposable
    {
        public void Attach(EmbySession session) { }
        public Task RefreshAsync() => Task.CompletedTask;
        public void Clear() { }
        public void Dispose() { }
    }
    internal sealed class SignInBoundary : Microsoft.UI.Xaml.FrameworkElement
    {
        internal Momoka.Shell.ViewModels.SignInViewModel ViewModel { get; } = new();
        internal event EventHandler? SignedIn;
        internal SignInBoundary() => ViewModel.SignedIn += (_, e) => SignedIn?.Invoke(this, e);
        public void Detach() => ViewModel.Detach();
        internal void Attach(ISettingsService settings, EmbySession session, CredentialVault vault) => ViewModel.Attach(settings, session, vault);
        internal void Prepare(ServerProfile? server = null, bool newAccount = false, AccountProfile? account = null) => ViewModel.Prepare(server, newAccount, account);
        internal void ShowRestoring(string name) => ViewModel.ShowRestoring(name);
    }
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
        private readonly ISettingsService? _settings;
        private readonly LoadGeneration _aggregateLoads = new();
        private readonly AccountModeBoundary _accountMode = new();
        private int _sourceOperations;
        private bool _pendingSourcesChange;
        private bool SignInVisible => SignIn.Visibility == Microsoft.UI.Xaml.Visibility.Visible;
        private ToggleBoundary AggregateModeItem { get; } = new();
        private MenuBoundary AccountMenu { get; } = new();
        private PlayerBoundary Player { get; } = new();
        private CloseBoundary? _settingsWindow = null;
        private CloseBoundary? _moviePilotWindow = null;
        private DispatchBoundary DispatcherQueue { get; } = new();
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

        internal ShellPage(EmbySession session, AppSettings settings, ISettingsService? settingsService = null,
            CredentialVault? vault = null, Momoka.Shell.Services.SubtitleTranslationController? translation = null)
        {
            _session = session;
            _capabilities = new CapabilitiesBoundary();
            _services = new ServicesBoundary(translation);
            Settings = settings;
            _settings = settingsService;
            if (settingsService is not null && vault is not null)
            {
                SignIn.Attach(settingsService, session, vault);
                SubscribeAccountEvents();
            }
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
        private sealed class ServicesBoundary(Momoka.Shell.Services.SubtitleTranslationController? translation) : IServiceProvider
        { public object? GetService(Type serviceType) => serviceType == typeof(Momoka.Shell.Services.SubtitleTranslationController) ? translation : throw new Momoka.Tests.AssertionException("夹具不创建产品服务"); }
    }
}
