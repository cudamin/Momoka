using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;
using Momoka.Updates;
using Windows.System;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Single inspect task held at concrete stages for real keyboard input from the coordinating run.</summary>
    private async Task KeyboardJourneyAsync(bool failFocusName = false)
    {
        using var fixture = CreateFixture();
        var server = new KeyboardJourneyProbeServer { Poster = await FixtureImageAsync(400, 600) };
        fixture.Transport.Reply = server.ReplyAsync;
        fixture.Settings.AggregateMode = true;
        fixture.Settings.Ui.SidebarMode = false;
        fixture.Settings.Ui.AnimationsEnabled = true;
        fixture.Settings.Ui.ShowHomeBanner = false;
        fixture.Settings.MoviePilot.Enabled = false;
        fixture.Settings.MoviePilot.AniBtEnabled = false;
        var originalMotion = HomeMotion.PreferenceEnabled;
        var shell = new ShellPage();
        var actions = new KeyboardJourneyActions(shell);
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        using var updates = new UpdateClient(new RejectNetwork());
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("键盘夹具禁止起播"),
            new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders)), allowPlayback: false);
        using var supplemental = new ServiceCollection().AddSingleton(new ShaderStaging(fixture.Settings)).AddSingleton(fonts)
            .AddSingleton(new AudioDeviceCatalogue(() => null)).AddSingleton(playback).AddSingleton(updates)
            .AddSingleton(new MoviePilotCredentials(PassthroughSecretProtector.Instance)).BuildServiceProvider();
        var services = new KeyboardJourneyServices(fixture.Services, supplemental, actions);
        shell.Attach(services);
        Field(shell, "_window", _window);
        var snapshot = new List<object>();
        var keys = 0;
        var lastKey = "";
        var run = Guid.NewGuid().ToString("N");
        SettingsWindow? settingsWindow = null;
        var topMost = _window!.TopMost;
        var witness = new KeyEventHandler((_, args) => { keys++; lastKey = args.Key.ToString(); });
        shell.AddHandler(UIElement.PreviewKeyDownEvent, witness, handledEventsToo: true);
        _root.Children.Add(shell);
        try
        {
            HomeMotion.ApplyPreference(true);
            _window.TopMost = true;
            ResizeProbeClient(1280, 800);
            shell.SignInRoot.Prepare();
            await LayoutAsync(shell);
            var signIn = shell.SignInRoot;
            var address = Get<TextBox>(signIn, "ServerBox");
            if (failFocusName) AutomationProperties.SetName(address, "错误名称夹具");
            await FocusAsync(address, "KeyboardJourneyAddress");
            await Stage("address-ready", "type https://keyboard-a.invalid then Enter", _window.Handle,
                () => signIn.ViewModel.Step == ViewModels.SignInStep.Credential && !signIn.ViewModel.Busy);
            Require(!signIn.ViewModel.NoticeOpen, "匿名连接没有成功到账号页");
            var user = Get<TextBox>(signIn, "UserBox");
            await FocusAsync(user, "KeyboardJourneyUsername");
            var password = Get<PasswordBox>(signIn, "PasswordInput");
            await Stage("username-ready", "Ctrl+A, type keyboard-user, then Enter", _window.Handle,
                () => signIn.ViewModel.Username == KeyboardJourneyProbeServer.Username && IsWithin(password));
            RequireFocus(password);
            AutomationProperties.SetAutomationId(password, "KeyboardJourneyPassword");
            await Stage("password-ready", "type fixture-password then Enter", _window.Handle,
                () => fixture.Session.IsSignedIn && !shell.SignInVisible && shell.Pages.Content is HomePage { IsReady: true });
            Require(server.AuthenticationPosts == 1, "真实表单未且仅提交一次匿名登录");
            await fixture.SelectAsync("keyboard-b.invalid", KeyboardJourneyProbeServer.Username);
            var second = fixture.Session.Capture();
            var first = fixture.Session.Sources.Single(scope => scope.Connection.ApiBase.Host == "keyboard-a.invalid");
            fixture.Session.SelectSource(first);
            await (Task)Call(shell, "EnterShellAsync")!;
            await LayoutAsync(shell);
            var account = Get<Button>(shell, "AccountButton");
            await FocusAsync(account, "KeyboardJourneyAccount");
            var accountMenu = Get<Flyout>(shell, "AccountMenu");
            await Stage("account-ready", "press Enter to open the account menu", _window.Handle,
                () => Get<FrameworkElement>(shell, "AccountMenuBody").IsLoaded && VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot).Count > 0);
            var select = Descendants(Get<FrameworkElement>(shell, "AccountMenuBody")).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "AccountSelect:" + second.Key);
            await FocusAsync(select, "KeyboardJourneySecondSource");
            await Stage("source-ready", "press Enter to select keyboard-b.invalid", _window.Handle,
                () => fixture.Session.Capture().Connection.ApiBase.Host == "keyboard-b.invalid" && shell.Pages.Content is HomePage { IsReady: true });
            Require(VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot).Count == 0, "选来源后菜单没关闭");
            var openSearch = Get<Button>(shell, "TitleSearchBox");
            await FocusAsync(openSearch, "KeyboardJourneyOpenSearch");
            await Stage("search-entry-ready", "press Enter to open search", _window.Handle,
                () => shell.Pages.Content is LibraryPage search && search.SearchState.IsSearch);
            var searchPage = (LibraryPage)shell.Pages.Content;
            var searchInput = Get<AutoSuggestBox>(searchPage, "SearchInput");
            await FocusAsync(searchInput, "KeyboardJourneySearch");
            await Stage("search-ready", "type keyboard-query then Enter", _window.Handle,
                () => !searchPage.ViewModel.Busy && searchPage.ViewModel.Cards.Count == 1
                    && searchPage.ViewModel.SearchText == KeyboardJourneyProbeServer.Keyword);
            Require(searchPage.ViewModel.Cards[0].Scope?.Connection.ApiBase.Host == "keyboard-b.invalid", "搜索结果没有保留目标来源");
            ResizeProbeClient(760, 760);
            HomeMotion.ApplyPreference(false);
            await LayoutAsync(searchPage);
            var result = Descendants(searchPage).OfType<Button>().FirstOrDefault(button => button.Content is PosterCard { Card.Item.Id: KeyboardJourneyProbeServer.MovieId });
            if (result is null)
            {
                // ItemsView's item container is the actual navigation target; its Enter invokes the card, never the playback overlay.
                var wall = Get<ItemsView>(searchPage, "CardWall");
                result = null;
                var container = KeyboardJourneyProbeRules.FindResult(Descendants(wall).OfType<ItemContainer>(),
                    item => Descendants(item).OfType<PosterCard>().FirstOrDefault()?.Card?.Item.Id,
                    KeyboardJourneyProbeServer.MovieId);
                Require(container is not null, "实际搜索结果没有可聚焦导航容器");
                await FocusAsync(container!, "KeyboardJourneyResult");
            }
            else await FocusAsync(result, "KeyboardJourneyResult");
            await Stage("result-ready", "press Enter to open the selected movie details", _window.Handle,
                () => shell.Pages.Content is DetailPage { IsReady: true });
            var detail = (DetailPage)shell.Pages.Content;
            Require(detail.ViewModel.CurrentItem?.Id == KeyboardJourneyProbeServer.MovieId && actions.Plays == 0, "结果Enter未进详情或触发播放");
            detail.AttachWindow(_window);
            var settingsButton = Get<Button>(shell, "SettingsButton");
            await FocusAsync(settingsButton, "KeyboardJourneyOpenSettings");
            await Stage("settings-entry-ready", "press Enter to open settings", _window.Handle,
                () => shell.SettingsRoot is { IsReady: true });
            settingsWindow = Get<SettingsWindow>(shell, "_settingsWindow");
            _openingProbeWindows.Add(settingsWindow);
            var settings = shell.SettingsRoot!;
            await LayoutAsync(settings);
            settings.AddHandler(UIElement.PreviewKeyDownEvent, witness, handledEventsToo: true);
            var settingsSearch = Get<AutoSuggestBox>(settings, "SettingsSearchBox");
            await FocusAsync(settingsSearch, "KeyboardJourneySettingsSearch");
            await Stage("settings-ready", "press Tab then Shift+Tab to return to the settings search field", settingsWindow.Handle,
                () => keys >= 2 && IsWithin(settingsSearch), minimumKeys: 2);
            RequireFocus(settingsSearch);
            await CompositionPlaybackProbe.SaveScreenAsync(settingsWindow.Handle, Path.Combine(_options.Paths.LogDirectory, "keyboard-journey-settings-760-screen.png"));
            await Stage("settings-close-ready", "press Alt+F4 to hide the settings window", settingsWindow.Handle,
                () => !settingsWindow.IsOpen, minimumKeys: 0);
            settings.RemoveHandler(UIElement.PreviewKeyDownEvent, witness);
            Require(ReferenceEquals(shell.Pages.Content, detail), "关闭设置改变了详情主体");
            _window.Activate();
            var back = Get<Button>(shell, "BackButton");
            await FocusAsync(back, "KeyboardJourneyBack");
            await Stage("back-ready", "press Alt+Left to return to search", _window.Handle,
                () => shell.Pages.Content is LibraryPage restored && restored.SearchState.IsSearch && restored.ViewModel.IsReady);
            var restoredSearch = (LibraryPage)shell.Pages.Content;
            KeyboardJourneyProbeRules.RequireFinal(Get<AutoSuggestBox>(restoredSearch, "SearchInput").Text,
                restoredSearch.ViewModel.Cards.All(card => card.Scope?.Connection.ApiBase.Host == "keyboard-b.invalid"),
                actions.Plays, server.AuthenticationPosts, server.RejectedWrites);
            await SaveProbeScreenAsync("keyboard-journey-return-760-screen.png");
            WriteStage("complete", "completed", _window.Handle);
        }
        finally
        {
            settingsWindow?.Hide();
            Call(shell, "StopMoviePilotMonitoring");
            Get<BrowsePresentation>(shell, "_browsePresentation").Stop();
            (shell.Pages.Content as IShellContent)?.Release();
            shell.SignInRoot.Detach();
            shell.RemoveHandler(UIElement.PreviewKeyDownEvent, witness);
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            await playback.ShutdownAsync();
            _window.TopMost = topMost;
            _window.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(originalMotion);
        }

        bool IsWithin(DependencyObject target)
        {
            for (var focus = FocusManager.GetFocusedElement(target is FrameworkElement frame ? frame.XamlRoot : shell.XamlRoot) as DependencyObject;
                focus is not null; focus = VisualTreeHelper.GetParent(focus))
                if (ReferenceEquals(focus, target)) return true;
            return false;
        }
        void RequireFocus(FrameworkElement target)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(target) ?? throw new InvalidOperationException("缺少自动化peer");
            try { KeyboardJourneyProbeRules.RequireFocus(peer.GetName(), peer.GetAutomationControlType().ToString(), IsWithin(target)); }
            catch (InvalidOperationException error)
            {
                var actual = FocusManager.GetFocusedElement(target.XamlRoot) as FrameworkElement;
                var bounds = target.TransformToVisual(target.XamlRoot.Content).TransformBounds(new Windows.Foundation.Rect(0, 0, target.ActualWidth, target.ActualHeight));
                throw new InvalidOperationException($"{error.Message}：target={target.Name}/{AutomationProperties.GetAutomationId(target)}，"
                    + $"name={peer.GetName()} role={peer.GetAutomationControlType()}，focus={actual?.Name}/{actual?.GetType().Name}，"
                    + $"within={IsWithin(target)} visible={target.Visibility} bounds={bounds.X:0.#},{bounds.Y:0.#},{bounds.Width:0.#},{bounds.Height:0.#}", error);
            }
        }
        async Task FocusAsync(Control target, string id)
        {
            AutomationProperties.SetAutomationId(target, id);
            await LayoutAsync(target);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                target.Focus(FocusState.Keyboard);
                await Task.Delay(50);
                if (IsWithin(target)) break;
            }
            RequireFocus(target);
            if (id == "KeyboardJourneyAddress")
                KeyboardJourneyProbeRules.RequireName(FrameworkElementAutomationPeer.CreatePeerForElement(target)!.GetName(), "服务器地址");
        }
        async Task Stage(string stage, string input, IntPtr hwnd, Func<bool> ready, int minimumKeys = 1)
        {
            var before = keys;
            WriteStage(stage, input, hwnd);
            for (var attempt = 0; attempt < 1200; attempt++)
            {
                if (keys >= before + minimumKeys && ready())
                {
                    if (minimumKeys > 0) KeyboardJourneyProbeRules.RequireKeyProgress(before, keys);
                    Read(stage + "-passed", hwnd);
                    return;
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("未完成真实键盘阶段：" + stage);
        }
        void WriteStage(string stage, string input, IntPtr hwnd)
        {
            Read(stage, hwnd);
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "keyboard-journey-stage.json"), JsonSerializer.Serialize(new
            { stage, input, process = Environment.ProcessId, hwnd = hwnd.ToInt64(), run, keys, physicalInputRequired = true }));
        }
        void Read(string stage, IntPtr hwnd)
        {
            var focus = FocusManager.GetFocusedElement(shell.SettingsRoot?.XamlRoot ?? shell.XamlRoot) as FrameworkElement;
            var peer = focus is null ? null : FrameworkElementAutomationPeer.CreatePeerForElement(focus);
            snapshot.Add(new
            {
                stage, process = Environment.ProcessId, hwnd = hwnd.ToInt64(), run, keys, lastKey,
                page = shell.Pages.Content?.GetType().Name, source = fixture.Session.IsSignedIn ? fixture.Session.Capture().Connection.ApiBase.Host : null,
                focus = focus?.Name, automationId = focus is null ? null : AutomationProperties.GetAutomationId(focus),
                automationName = peer?.GetName(), role = peer?.GetAutomationControlType().ToString(),
                programmaticFocus = true, physicalKeysObserved = keys > 0, narratorTested = false,
                plays = actions.Plays, server.AuthenticationPosts, server.RejectedWrites
            });
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "keyboard-journey-receipt.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class KeyboardJourneyServices(IServiceProvider core, IServiceProvider supplemental, IShellActions actions) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IShellActions) ? actions : supplemental.GetService(serviceType) ?? core.GetService(serviceType);
    }

    private sealed class KeyboardJourneyActions(ShellPage shell) : IShellActions
    {
        private IShellActions Navigation => shell;
        internal int Plays { get; private set; }
        public void OpenItem(EmbyItem item)
        {
            if (item.Type != EmbyItemType.Movie) throw new InvalidOperationException("键盘夹具只打开影片详情");
            Navigation.OpenItem(item);
        }
        public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null)
        { Plays++; throw new InvalidOperationException("键盘夹具禁止播放"); }
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => Navigation.Notify(message, severity);
        public void ReportMarkedWatched(EmbyItem item) => throw new InvalidOperationException("键盘夹具禁止写入");
        public void RefreshFavorites(EmbyItem? changed = null) => Navigation.RefreshFavorites(changed);
        public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => Navigation.TryOpenLibrary(id, source);
        public void OpenGenre(string genre, EmbySessionScope? source = null) => Navigation.OpenGenre(genre, source);
        public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) => Navigation.OpenRow(target, source);
        public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) => Navigation.OpenSignIn(server, newAccount);
        public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account) => Navigation.SwitchProfileAsync(server, account);
        public void SetTitleStrip(TitleStrip strip) => Navigation.SetTitleStrip(strip);
        public bool MoviePilotEnabled => false;
        public bool AniBtEnabled => false;
        public bool MoviePilotMediaManagementEnabled => false;
        public bool MoviePilotSmartReorganizeEnabled => false;
        public event EventHandler? MoviePilotAvailabilityChanged { add { } remove { } }
        public void OpenMoviePilotMedia(MoviePilotMedia media, MoviePilotSubscription? subscription = null) => throw new InvalidOperationException("键盘夹具不打开MP");
        public void SearchMoviePilotVersions(EmbyItem item) => throw new InvalidOperationException("键盘夹具不查询MP");
        public void SearchAniBtVersions(EmbyItem item) => throw new InvalidOperationException("键盘夹具不查询AniBT");
        public void ShowMoviePilotReorganize(MoviePilotTransferContext context) => throw new InvalidOperationException("键盘夹具禁止写入");
        public void ShowMoviePilotHistoryAction(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action) => throw new InvalidOperationException("键盘夹具禁止写入");
        public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => throw new InvalidOperationException("键盘夹具禁止写入");
        public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) => throw new InvalidOperationException("键盘夹具不打开缺集");
    }
}
