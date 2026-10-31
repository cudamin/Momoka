using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>供真实鼠标／键盘操作的完整 Shell。业务页面和返回栈照常运行，网络与播放停在匿名边界。</summary>
    private async Task<MoviePilotJourneySession> InspectMoviePilotJourneyAsync(Frame host)
    {
        var transport = new MoviePilotJourneyServer { Poster = await FixtureImageAsync(400, 600) };
        var fixture = CreateFixture(transport);
        var shell = new ShellPage();
        var session = new MoviePilotJourneySession(host, shell, fixture, transport, _options.Paths.LogDirectory);
        try
        {
            await fixture.SelectAsync();
            fixture.Settings.Ui.SidebarMode = true;
            fixture.Settings.MoviePilot.AniBtEnabled = false;
            fixture.Transport.Reply = (request, _) =>
            {
                // 假媒体库没有可播放条目；访问 Images 也只给本地生成图。
                Require(request.Method == HttpMethod.Get, "MP 检查台不写入媒体库");
                var path = request.RequestUri!.AbsolutePath;
                return Task.FromResult(path.Contains("/Images/", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(transport.Poster) }
                    : path.EndsWith("/Items", StringComparison.Ordinal) || path.EndsWith("/Episodes", StringComparison.Ordinal)
                        ? Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }) : FakeTransport.Standard(request));
            };
            await ConnectMoviePilotAsync(fixture);
            shell.Attach(new MoviePilotJourneyServices(fixture.Services, new MoviePilotJourneyActions(shell)));
            session.Attached = true;
            Field(shell, "_window", _window);
            if (host.Content is IShellContent previous) previous.Release();
            host.Content = shell;
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            shell.OpenSearch();
            await LayoutAsync(shell);
            session.Capture();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>拥有这一次检查的服务与页面；外层检查台仍负责八分钟驻留、窗口大小与结束按钮。</summary>
    private sealed class MoviePilotJourneySession : IDisposable
    {
        private readonly Frame _host;
        private readonly ShellPage _shell;
        private readonly Fixture _fixture;
        private readonly MoviePilotJourneyServer _transport;
        private readonly string _directory;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly List<string> _navigation = [];
        private bool _disposed;
        internal bool Attached { get; set; }

        internal MoviePilotJourneySession(Frame host, ShellPage shell, Fixture fixture, MoviePilotJourneyServer transport, string directory)
        {
            _host = host;
            _shell = shell;
            _fixture = fixture;
            _transport = transport;
            _directory = directory;
            shell.Pages.Navigated += OnNavigated;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnNavigated(object sender, NavigationEventArgs args)
        {
            // Shell 给订阅详情的原播放回调绕过 IShellActions 服务；导航完成的同一拍撤掉它，
            // 不让手工检查误触播放，也不靠捕获真正起播后的异常保护真实记录。
            if (args.Content is MoviePilotSubscriptionPage detail)
                detail.ViewModel.AttachPlayback(null, _ => Task.FromException(new InvalidOperationException("MP 检查台禁止播放")));
            _navigation.Add(args.Content?.GetType().Name ?? "empty");
        }

        private void OnTick(object? sender, object args)
        {
            if (_disposed) return;
            try { Capture(); }
            catch (IOException error) { Diagnostics.Log.Warn("probe", "MP 检查台状态记录失败", error); }
        }

        internal void Capture()
        {
            if (_disposed) return;
            var page = _shell.Pages.Content as FrameworkElement;
            var search = page as LibraryPage;
            var panel = search is null ? null : Get<MoviePilotSearchPanel>(search, "MoviePilotPanel");
            var dialogs = _shell.XamlRoot is null ? [] : VisualTreeHelper.GetOpenPopupsForXamlRoot(_shell.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>()
                .Select(dialog => dialog.Title?.ToString() ?? dialog.GetType().Name).Distinct().ToArray();
            var receipt = _transport.Receipt;
            var snapshot = new
            {
                process = Environment.ProcessId,
                page = page?.GetType().Name,
                tag = page?.Tag?.ToString(),
                navigation = _navigation.ToArray(),
                back = _shell.Pages.BackStack.Count,
                forward = _shell.Pages.ForwardStack.Count,
                search = search is null ? null : new
                {
                    keyword = Get<AutoSuggestBox>(search, "SearchInput").Text,
                    source = Get<CommunityToolkit.WinUI.Controls.Segmented>(search, "SourceTabs").SelectedIndex,
                    titles = panel!.ViewModel.Results.Select(result => result.Title).ToArray(),
                    resultCount = panel.ViewModel.Results.Count,
                    busy = panel.ViewModel.Busy
                },
                dialogs,
                requests = receipt.GetProperty("requests"),
                writes = receipt.GetProperty("writes"),
                writeBodies = receipt.GetProperty("writeBodies"),
                subscriptions = receipt.GetProperty("subscriptions"),
                rejected = receipt.GetProperty("rejected")
            };
            File.WriteAllText(Path.Combine(_directory, "moviepilot-journey-state.json"),
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _timer.Stop();
            _timer.Tick -= OnTick;
            try { Capture(); }
            finally
            {
                _disposed = true;
                _shell.Pages.Navigated -= OnNavigated;
                try
                {
                    if (Attached)
                    {
                        if (_shell.XamlRoot is { } root)
                            foreach (var dialog in VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
                                .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray()) dialog.Hide();
                        _fixture.Settings.MoviePilot.Enabled = false;
                        Call(_shell, "StopMoviePilotMonitoring");
                        Call(_shell, "ReleaseContent");
                        _shell.SignInRoot.Detach();
                    }
                }
                finally
                {
                    Field(_shell, "_window", null);
                    if (ReferenceEquals(_host.Content, _shell)) _host.Content = null;
                    _fixture.Dispose();
                    _transport.Dispose();
                }
            }
        }
    }

    private sealed class MoviePilotJourneyServices(IServiceProvider services, IShellActions actions) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IShellActions) ? actions : services.GetService(serviceType);
    }

    /// <summary>只把浏览动作转给真 Shell；播放、切真实身份及整理入口始终拒绝。</summary>
    private sealed class MoviePilotJourneyActions(ShellPage shell) : IShellActions
    {
        private IShellActions Navigation => shell;
        private static InvalidOperationException Refused() => new("MP 检查台仅供浏览与匿名订阅操作");
        public void OpenItem(EmbyItem item) => Navigation.OpenItem(item);
        public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null) => Task.FromException(Refused());
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => Navigation.Notify(message, severity);
        public void ReportMarkedWatched(EmbyItem item) => throw Refused();
        public void RefreshFavorites(EmbyItem? changed = null) => Navigation.RefreshFavorites(changed);
        public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => Navigation.TryOpenLibrary(id, source);
        public void OpenGenre(string genre, EmbySessionScope? source = null) => Navigation.OpenGenre(genre, source);
        public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) => Navigation.OpenRow(target, source);
        public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) => throw Refused();
        public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account) => Task.FromException(Refused());
        public void SetTitleStrip(TitleStrip strip) => Navigation.SetTitleStrip(strip);
        public bool MoviePilotEnabled => Navigation.MoviePilotEnabled;
        public bool AniBtEnabled => false;
        public bool MoviePilotMediaManagementEnabled => false;
        public bool MoviePilotSmartReorganizeEnabled => false;
        public event EventHandler? MoviePilotAvailabilityChanged
        {
            add => Navigation.MoviePilotAvailabilityChanged += value;
            remove => Navigation.MoviePilotAvailabilityChanged -= value;
        }
        public void OpenMoviePilotMedia(MoviePilotMedia media, MoviePilotSubscription? subscription = null) => Navigation.OpenMoviePilotMedia(media, subscription);
        public void SearchMoviePilotVersions(EmbyItem item) => Navigation.SearchMoviePilotVersions(item);
        public void SearchAniBtVersions(EmbyItem item) => throw Refused();
        public void ShowMoviePilotReorganize(MoviePilotTransferContext context) => throw Refused();
        public void ShowMoviePilotHistoryAction(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action) => throw Refused();
        public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => throw Refused();
        public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) => throw Refused();
    }

    /// <summary>组合现有订阅假服务器与搜索／编辑目录；没有真实 HTTP handler，未知写请求也不能成功。</summary>
    private sealed class MoviePilotJourneyServer : HttpMessageHandler
    {
        internal SubscriptionProbeServer Subscriptions { get; } = new();
        private readonly HttpMessageInvoker _subscriptions;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _receiptGate = new();
        private JsonElement _receipt;
        internal JsonElement Receipt { get { lock (_receiptGate) return _receipt; } }
        internal List<string> Requests { get; } = [];
        internal int Rejected { get; private set; }
        internal bool FailNextCreate { get; set; }
        internal byte[] Poster { get => Subscriptions.Poster; set => Subscriptions.Poster = value; }

        internal MoviePilotJourneyServer()
        {
            _subscriptions = new HttpMessageInvoker(Subscriptions, disposeHandler: false);
            Subscriptions.Items.Remove(5);
            var first = Subscriptions.Items[4];
            first["keyword"] = "漫长的旅途";
            first["sites"] = new System.Text.Json.Nodes.JsonArray(1);
            first["quality"] = "WEB-?DL|WEB-?RIP";
            first["resolution"] = "1080[pi]|x1080";
            first["downloader"] = "PT";
            first["save_path"] = "/downloads/series";
            first["search_interval"] = null;
            first["best_version"] = 0;
            first["best_version_full"] = 0;
            first["search_imdbid"] = 0;
            UpdateReceipt();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try { return await HandleAsync(request, cancellationToken); }
            finally
            {
                UpdateReceipt();
                _gate.Release();
            }
        }

        private void UpdateReceipt()
        {
            var receipt = JsonSerializer.SerializeToElement(new
            {
                requests = Requests.ToArray(),
                writes = Subscriptions.Writes.ToArray(),
                writeBodies = Subscriptions.WriteBodies.ToArray(),
                subscriptions = Subscriptions.Items.Values.ToArray(),
                rejected = Rejected
            });
            lock (_receiptGate) _receipt = receipt;
        }

        private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri is not { Scheme: "https", Host: "moviepilot.invalid", Port: 3001 } uri
                || !uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal))
                return Reject();
            var path = uri.AbsolutePath;
            Requests.Add(request.Method + " " + uri.PathAndQuery);
            if (request.Method == HttpMethod.Post && path == "/api/v1/subscribe/" && FailNextCreate)
            {
                FailNextCreate = false;
                return Json(new { success = false, message = "匿名夹具主动拒绝本次订阅，尚未写入" });
            }
            if (request.Method != HttpMethod.Get && !AllowedWrite(request.Method, path)) return Reject();
            if (request.Method == HttpMethod.Get)
            {
                if (path == "/api/v1/media/source") return SearchProbeServer.Sources();
                if (path == "/api/v1/media/search") return Json(new object[]
                {
                    new { title = "漫长的旅途", year = "2026", type = "电视剧", media_source = "themoviedb", media_id = "4", tmdb_id = 4,
                        poster_path = "", vote_average = 8.5 },
                    new { title = "星际远航", year = "2026", type = "电影", media_source = "themoviedb", media_id = "1", tmdb_id = 1,
                        poster_path = "", vote_average = 8.1 }
                });
                if (path is "/api/v1/site/" or "/api/v1/site/rss" or "/api/v1/site/media/movie" or "/api/v1/site/media/tv") return Json(new[]
                {
                    new { id = 1, name = "站点甲", is_active = true }, new { id = 2, name = "站点乙", is_active = true },
                    new { id = 3, name = "已停用站点", is_active = false }
                });
                if (path is "/api/v1/search/title" or "/api/v1/search/media/4" or "/api/v1/search/media/1") return Json(Array.Empty<object>());
                if (path == "/api/v1/download/clients") return Json(new[] { new { name = "PT" }, new { name = "qbittorrent" } });
                if (path == "/api/v1/download/paths") return Json(new[]
                {
                    new { name = "电视剧", download_path = "/downloads/series", save_path = "local:/downloads/series" },
                    new { name = "动画", download_path = "/downloads/anime", save_path = "local:/downloads/anime" },
                    new { name = "剧集备用别名", download_path = "/downloads/series", save_path = "local:/downloads/series" }
                });
                if (path == "/api/v1/rule/groups") return Json(new { rule_groups = new[] { new { name = "剧集优先" }, new { name = "高清优先" } } });
                if (!AllowedSubscriptionRead(path)) return Reject();
            }
            return await _subscriptions.SendAsync(request, cancellationToken);
        }

        private HttpResponseMessage Reject()
        {
            Rejected++;
            throw new InvalidOperationException("MP 检查台拒绝未登记的目标或操作");
        }

        private static bool AllowedWrite(HttpMethod method, string path) =>
            method == HttpMethod.Post && path is "/api/v1/login/access-token" or "/api/v1/mediaserver/exists_remote" or "/api/v1/subscribe/"
            || method == HttpMethod.Put && path == "/api/v1/subscribe/"
            || method == HttpMethod.Delete && IsNumberSuffix(path, "/api/v1/subscribe/");

        private static bool AllowedSubscriptionRead(string path) =>
            path is "/api/v1/dashboard/system" or "/api/v1/user/current" or "/api/v1/system/global/user"
                or "/api/v1/system/setting/public/DefaultTvSubscribeConfig" or "/api/v1/system/cache/image"
                or "/api/v1/subscribe/" or "/api/v1/media/1" or "/api/v1/media/4" or "/api/v1/media/seasons"
                or "/api/v1/media/group/seasons/grp-9" or "/api/v1/media/group/seasons/grp-10"
            || IsNumberSuffix(path, "/api/v1/subscribe/") || IsNumberSuffix(path, "/api/v1/subscribe/files/")
            || IsNumberSuffix(path, "/api/v1/media/groups/") || path.StartsWith("/api/v1/tmdb/", StringComparison.Ordinal);

        private static bool IsNumberSuffix(string path, string prefix) => path.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(path.AsSpan(prefix.Length), out var id) && id > 0;

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _subscriptions.Dispose(); Subscriptions.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
