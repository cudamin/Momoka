using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Anonymous HTTP and live WinUI controls; never restores production settings or plays media.</summary>
    private async Task PlexUserStateAsync()
    {
        var evidence = new List<object>();
        foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Forbidden })
            foreach (var played in new[] { true, false })
                await CheckCardAsync(status, played);
        foreach (var width in new[] { 1320, 760 })
            foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Forbidden })
                foreach (var played in new[] { true, false })
                    await CheckDetailAsync(width, status, played);
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "plex-state-receipt.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

        async Task CheckCardAsync(HttpStatusCode status, bool played)
        {
            using var fixture = CreateFixture();
            var server = new PlexStateProbeServer(!played);
            fixture.Transport.Reply = server.ReplyAsync;
            await fixture.SelectAsync("plex-state.invalid", kind: MediaServerKind.Plex);
            var shell = NotificationHost();
            var actions = new PlexStateProbeActions(shell);
            var scope = fixture.Session.Capture();
            var seed = scope.Bind(server.Seed());
            var card = new CardItem(seed, fixture.Images, 240);
            var poster = new PosterCard { Card = card };
            poster.ActionRequested += (_, args) => ItemCommands.Handle(fixture.Session, actions, poster, args);
            var frame = Get<Frame>(shell, "ContentFrame");
            frame.Content = new StackPanel { Margin = new Thickness(36, 70, 36, 0), Children = { poster } };
            ResizeProbeClient(760, 900);
            _root.Children.Add(shell);
            try
            {
                await LayoutAsync(shell);
                Require(poster.IsLoaded && poster.ActualWidth > 0 && card.Watched == !played,
                    "Plex 卡片未实际加载或初始状态错误");
                server.ReadFailure = status;
                // Execute the real card click handler and event wiring without moving the user's pointer.
                Call(poster, "OnWatchedClicked", Get<Button>(poster, "WatchedButton"), new RoutedEventArgs());
                await UntilAsync(() => server.Reads == 1 && PendingPlexState(card) == 0);
                var toast = Get<InfoBar>(shell, "Toast");
                Require(card.Watched == played && seed.IsWatched == played,
                    "已确认 Plex 写入在补读失败后丢失卡片状态");
                Require(server.Writes == 1 && server.Reads == 1, "Plex 卡片补读重新发送了写入");
                var badge = Descendants(poster).OfType<FontIcon>().Single(icon => icon.Glyph == "\uE73E");
                Require(badge.Parent is Border border && border.Visibility == (played ? Visibility.Visible : Visibility.Collapsed),
                    "Plex 卡片已看角标没有跟随真实绑定更新");
                Require(toast.IsOpen && toast.Severity == Severity(status)
                    && toast.Message.Contains("已保存", StringComparison.Ordinal)
                    && toast.Message.Contains("刷新失败", StringComparison.Ordinal), "卡片没有区分保存成功与刷新失败");
                Require(actions.WatchedReports == (played ? 1 : 0), "Plex 已看附带回调次数错误");
                await LayoutAsync(toast);
                Require(toast.ActualWidth > 0 && toast.ActualHeight > 0, "卡片刷新通知没有实际布局");
                await SaveProbeScreenAsync($"plex-state-card-{(int)status}-{played}-screen.png");
                evidence.Add(new
                {
                    Surface = "card",
                    Status = (int)status,
                    Target = played,
                    server.Writes,
                    server.Reads,
                    Confirmed = card.Watched,
                    Notice = toast.Message,
                    Severity = toast.Severity.ToString()
                });
            }
            finally
            {
                Get<DispatcherTimer>(shell, "_toast").Stop();
                frame.Content = null;
                _root.Children.Remove(shell);
            }
        }

        async Task CheckDetailAsync(int width, HttpStatusCode status, bool played)
        {
            using var fixture = CreateFixture();
            var server = new PlexStateProbeServer(!played);
            fixture.Transport.Reply = server.ReplyAsync;
            await fixture.SelectAsync("plex-state.invalid", kind: MediaServerKind.Plex);
            var shell = NotificationHost();
            var actions = new PlexStateProbeActions(shell);
            var services = new PlexStateProbeServices(fixture.Services, actions);
            var scope = fixture.Session.Capture();
            var seed = scope.Bind(server.Seed());
            var frame = Get<Frame>(shell, "ContentFrame");
            ResizeProbeClient(width, 900);
            _root.Children.Add(shell);
            DetailPage? page = null;
            try
            {
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(services, seed)), "Plex 匿名详情导航失败");
                page = (DetailPage)frame.Content;
                page.AttachWindow(_window!);
                await UntilAsync(() => page.IsReady && page.CurrentItem is not null);
                await LayoutAsync(page);
                Require(!page.ViewModel.NoticeOpen && page.ViewModel.ToggleWatchedCommand.CanExecute(null),
                    "Plex 初次详情未成功或生成命令主体未就绪");
                Require(page.ViewModel.IsCompact == (width == 760), "Plex 详情未进入指定宽窄版式");
                var handle = width == 760 ? "CompactWatchedButton" : "ColumnWatchedButton";
                var watchedButton = Descendants(page).OfType<Button>().Single(button =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == handle);
                Require(watchedButton.ActualWidth > 0 && watchedButton.IsEnabled
                    && ReferenceEquals(watchedButton.Command, page.ViewModel.ToggleWatchedCommand),
                    "真实详情已看按钮没有绑定可执行生成命令");
                Require(!ReferenceEquals(seed, page.CurrentItem), "HTTP 详情必须是独立 DTO，不能共享种子遮住刷新缺陷");
                var initialReads = server.Reads;
                server.ReadFailure = status;
                await page.ViewModel.ToggleWatchedCommand.ExecuteAsync(null);
                await LayoutAsync(page);
                Require(page.ViewModel.Watched == played && page.CurrentItem!.IsWatched == played,
                    "已确认 Plex 写入在失败刷新后丢失详情状态");
                Require(server.Writes == 1 && server.Reads == initialReads + 1,
                    "Plex 详情补读重新发送了写入或读数异常");
                var bar = Descendants(page).OfType<InfoBar>().Single();
                Require(bar.IsOpen && bar.Severity == Severity(status)
                    && bar.Title.Contains("已保存", StringComparison.Ordinal)
                    && bar.Title.Contains("刷新", StringComparison.Ordinal), "详情没有显示已保存但刷新失败的真实 InfoBar");
                Require(actions.WatchedReports == (played ? 1 : 0), "详情确认已看附带回调次数错误");
                var labels = ((StackPanel)watchedButton.Content).Children.OfType<TextBlock>()
                    .Where(label => label.Visibility == Visibility.Visible).ToArray();
                Require(labels.Length == 1 && labels[0].Text == (played ? "已看" : "未看"),
                    "详情已看按钮文字没有随确认状态更新：" + string.Join(" / ", labels.Select(label => label.Text)));
                await SaveProbeScreenAsync($"plex-state-detail-{width}-{(int)status}-{played}-confirmed-screen.png");
                await page.ScrollToEndAsync();
                await LayoutAsync(page);
                Require(bar.ActualWidth > 0 && bar.ActualHeight > 0, "详情刷新失败 InfoBar 没有实际布局");
                await SaveProbeScreenAsync($"plex-state-detail-{width}-{(int)status}-{played}-screen.png");
                evidence.Add(new
                {
                    Surface = "detail",
                    Width = width,
                    Status = (int)status,
                    Target = played,
                    server.Writes,
                    server.Reads,
                    Confirmed = page.ViewModel.Watched,
                    Notice = bar.Title + "：" + bar.Message,
                    Severity = bar.Severity.ToString()
                });
            }
            finally
            {
                page?.Release();
                frame.Content = null;
                Get<DispatcherTimer>(shell, "_toast").Stop();
                _root.Children.Remove(shell);
            }
        }

        static InfoBarSeverity Severity(HttpStatusCode status) => status == HttpStatusCode.ServiceUnavailable
            ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
        static ShellPage NotificationHost()
        {
            // Only the shipped notification surface and content frame are mounted. This host is never
            // attached to playback services, accounts, normal startup or an activation listener.
            var shell = new ShellPage();
            Get<FrameworkElement>(shell, "SignIn").Visibility = Visibility.Collapsed;
            Get<FrameworkElement>(shell, "ContentHost").Visibility = Visibility.Visible;
            return shell;
        }
    }

    private static int PendingPlexState(CardItem card) => new[] { "_watched", "_favorite" }
        .Sum(name => (int)Get<object>(card.UserStateOrder, name).GetType().GetField("Pending")!
            .GetValue(Get<object>(card.UserStateOrder, name))!);

    private sealed class PlexStateProbeServer(bool initialPlayed)
    {
        private bool _played = initialPlayed;
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public HttpStatusCode? ReadFailure { get; set; }
        public EmbyItem Seed() => new()
        {
            Id = "state-item",
            Name = "匿名 Plex 观看确认",
            Type = EmbyItemType.Movie,
            Overview = "只使用内存假服务器；保存成功与补充刷新失败分别反馈。",
            UserData = new EmbyUserData { Played = _played, PlaybackPositionTicks = 40_000, PlayCount = 2 }
        };
        public Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            Require(request.Method == HttpMethod.Get, "Plex 观看探针拒绝非既有 GET 请求");
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return Reply(new(HttpStatusCode.NotFound));
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal)) return Reply(new(HttpStatusCode.NotFound));
            if (path.EndsWith("/identity", StringComparison.Ordinal)) return Reply(Json(new
            { MediaContainer = new { machineIdentifier = "plex-state-machine", version = "1.41.2" } }));
            if (path.EndsWith("/library/sections", StringComparison.Ordinal)) return Reply(Json(new
            { MediaContainer = new { Directory = Array.Empty<object>() } }));
            if (path.EndsWith("/:/scrobble", StringComparison.Ordinal) || path.EndsWith("/:/unscrobble", StringComparison.Ordinal))
            {
                Require(Query(request, "key") == "state-item", "Plex 探针写入了其他条目");
                Writes++;
                _played = path.EndsWith("/:/scrobble", StringComparison.Ordinal);
                return Reply(new(HttpStatusCode.NoContent));
            }
            if (path.EndsWith("/library/metadata/state-item", StringComparison.Ordinal))
            {
                Reads++;
                if (ReadFailure is { } failure) return Reply(new(failure));
                return Reply(Json(new
                {
                    MediaContainer = new
                    {
                        Metadata = new[] { new
                    { ratingKey = "state-item", title = "匿名 Plex 观看确认", type = "movie", duration = 1_200_000,
                        summary = Seed().Overview, viewCount = _played ? 1 : 0, viewOffset = 4 } }
                    }
                }));
            }
            if (path.EndsWith("/library/metadata/state-item/similar", StringComparison.Ordinal))
                return Reply(Json(new { MediaContainer = new { Metadata = Array.Empty<object>() } }));
            Require(path is "/media" or "/media/", "Plex 探针出现未登记端点：" + path);
            return Reply(Json(new
            {
                MediaContainer = new
                { machineIdentifier = "plex-state-machine", friendlyName = "匿名 Plex", version = "1.41.2" }
            }));
        }
        private static Task<HttpResponseMessage> Reply(HttpResponseMessage response) => Task.FromResult(response);
    }

    private sealed class PlexStateProbeServices(IServiceProvider services, IShellActions actions) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(IShellActions) ? actions : services.GetService(type);
    }

    private sealed class PlexStateProbeActions(ShellPage shell) : IShellActions
    {
        public int WatchedReports { get; private set; }
        private static InvalidOperationException Refused() => new("Plex 状态探针拒绝播放、外部动作和真实账号");
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => shell.Notify(message, severity);
        public void ReportMarkedWatched(EmbyItem item) => WatchedReports++;
        public void SetTitleStrip(TitleStrip strip) => shell.SetTitleStrip(strip);
        public bool MoviePilotEnabled => false;
        public bool AniBtEnabled => false;
        public bool MoviePilotMediaManagementEnabled => false;
        public bool MoviePilotSmartReorganizeEnabled => false;
        public event EventHandler? MoviePilotAvailabilityChanged { add { } remove { } }
        public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null,
            IReadOnlyList<EmbyItem>? episodes = null) => Task.FromException(Refused());
        public void OpenItem(EmbyItem item) => throw Refused();
        public void RefreshFavorites(EmbyItem? changed = null) => throw Refused();
        public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => throw Refused();
        public void OpenGenre(string genre, EmbySessionScope? source = null) => throw Refused();
        public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) => throw Refused();
        public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) => throw Refused();
        public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account) => Task.FromException(Refused());
        public void OpenMoviePilotMedia(MoviePilotMedia media, MoviePilotSubscription? subscription = null) => throw Refused();
        public void SearchMoviePilotVersions(EmbyItem item) => throw Refused();
        public void SearchAniBtVersions(EmbyItem item) => throw Refused();
        public void ShowMoviePilotReorganize(MoviePilotTransferContext context) => throw Refused();
        public void ShowMoviePilotHistoryAction(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action) => throw Refused();
        public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => throw Refused();
        public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) => throw Refused();
    }
}
