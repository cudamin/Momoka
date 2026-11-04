using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotMediaManagementAsync()
    {
        await MoviePilotEpisodeDeleteAsync();
        var transport = new MediaManagementProbeServer();
        using var fixture = CreateFixture(transport);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        await service.HistoryCapabilitiesAsync(CancellationToken.None);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/mp-movie", StringComparison.Ordinal)) return Task.FromResult(Json(Subject(EmbyItemType.Movie)));
            if (path.EndsWith("/Items/mp-series", StringComparison.Ordinal)) return Task.FromResult(Json(Subject(EmbyItemType.Series)));
            if (path.EndsWith("/Items", StringComparison.Ordinal) && Query(request, "ParentId") == "mp-series")
                return Task.FromResult(Json(new { Items = new[] { Subject(EmbyItemType.Episode) }, TotalRecordCount = 1 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        var anchor = new Button
        {
            Content = "MP 媒体管理离线菜单",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0)
        };
        _root.Children.Add(shell);
        _root.Children.Add(anchor);
        MenuFlyout? menu = null;
        MoviePilotMediaManagementDialog? dialog = null;
        var originalTheme = ThemeHost.Current;
        var topMost = _window!.TopMost;
        try
        {
            // 菜单打开前置顶，避免截图抬高宿主时把原生子菜单压到后面。
            _window.TopMost = true;
            foreach (var theme in Momoka.Theming.UiThemes.All)
                foreach (var width in new[] { 1280, 760 })
                {
                    ThemeHost.Apply(theme);
                    ResizeInspect(width, 900);
                    await LayoutAsync(anchor);
                    var movie = new CardItem(fixture.Session.Capture().Bind(Subject(EmbyItemType.Movie)), fixture.Images, 170);
                    menu = ItemCommands.Build(fixture.Session, shell, anchor, movie);
                    var reorganize = menu.Items.OfType<MenuFlyoutSubItem>().Single(row => row.Tag is ItemCommand.MoviePilotReorganizeMenu);
                    var management = menu.Items.OfType<MenuFlyoutSubItem>().Single(row => row.Tag is ItemCommand.MoviePilotMediaManagement);
                    Require(menu.Items.IndexOf(management) == menu.Items.IndexOf(reorganize) + 1, "媒体管理没有紧接重新整理");
                    Require(reorganize.Text == "重新整理" && reorganize.Items.Count == 2 && reorganize.Items.All(row => row is MenuFlyoutItem),
                        "电影的手动整理与智能整理未折叠进重新整理");
                    Require(management.Items.Count == 4 && management.Items.All(row => row is MenuFlyoutItem), "四个删除范围未折叠");
                    Require(reorganize.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand.MoviePilotSmartReorganize).Text == "智能整理", "电影智能整理措辞不正确");
                    menu.ShowAt(anchor);
                    await LayoutAsync(anchor);
                    foreach (var row in menu.Items.Where(row => row.Tag is ItemCommand cmd && ItemMenu.IsMoviePilot(cmd)))
                        AssertNoBrand((FrameworkElement)row);
                    await Task.Delay(350);
                    await SaveProbeScreenAsync($"mp-media-menu-{theme.Id}-{width}.png");
                    ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(reorganize).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                    await LayoutAsync(anchor);
                    foreach (var row in reorganize.Items) AssertNoBrand((FrameworkElement)row);
                    Require(reorganize.Items.OfType<MenuFlyoutItem>().Select(row => row.Text).SequenceEqual(new[]
                    { "手动整理…", "智能整理" }), "重新整理子菜单缺手动整理或智能整理");
                    await Task.Delay(350);
                    await SaveProbeScreenAsync($"mp-media-reorganize-submenu-{theme.Id}-{width}.png");
                    ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(reorganize).GetPattern(PatternInterface.ExpandCollapse)).Collapse();
                    ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(management).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                    await LayoutAsync(anchor);
                    foreach (var row in management.Items) AssertNoBrand((FrameworkElement)row);
                    Require(management.Items.OfType<MenuFlyoutItem>().Select(row => row.Text).SequenceEqual(new[]
                    { "仅删除记录", "删记录和源文件", "删记录和库文件", "删除记录及两端" }), "删除范围文案不一致");
                    await Task.Delay(350);
                    await SaveProbeScreenAsync($"mp-media-submenu-{theme.Id}-{width}.png");
                    ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(management).GetPattern(PatternInterface.ExpandCollapse)).Collapse();
                    menu.Hide();
                    await SettleAsync();
                }
            Require(transport.Writes == 0, "打开菜单发出了副作用请求");
            transport.Type = "电影";
            dialog = new MoviePilotMediaManagementDialog(service, fixture.Session.Capture(), Subject(EmbyItemType.Movie), MoviePilotHistoryAction.DeleteAll) { XamlRoot = _root.XamlRoot };
            var showing = dialog.ShowAsync().AsTask();
            await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
            Require(dialog.DefaultButton == ContentDialogButton.Close && transport.Writes == 0, "准备范围时发出删除或默认 Enter 确认删除");
            Require(dialog.Records.All(row => row.Media.Selected && row.SourceStatus == "将删除"), "确认没有默认全选或源文件范围错误");
            ResizeInspect(1040, 800);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(dialog);
                Require(BrushColor(dialog.Background).A > 0 &&
                    Get<TextBlock>(dialog, "SummaryText").Foreground is SolidColorBrush { Color.A: > 0 }, "确认窗口缺主题底色或文字色");
                await SaveProbeScreenAsync($"mp-media-delete-confirm-{theme.Id}.png");
            }
            ThemeHost.Apply(originalTheme);
            await Task.Delay(350);
            await SaveProbeScreenAsync("mp-media-delete-confirm.png");
            InvokeDialog(dialog, "CloseButton");
            await showing;
            Require(transport.Writes == 0, "取消确认仍发出删除");
            Require(!dialog.LibraryFilesRemovalSubmitted, "取消确认仍标记了删除媒体库文件");

            dialog = new MoviePilotMediaManagementDialog(service, fixture.Session.Capture(), Subject(EmbyItemType.Movie), MoviePilotHistoryAction.DeleteAll) { XamlRoot = _root.XamlRoot };
            showing = dialog.ShowAsync().AsTask();
            await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
            InvokeDialog(dialog, "PrimaryButton");
            await UntilAsync(() => transport.Writes == 1 && dialog.PrimaryButtonText.Length == 0);
            Require(dialog.LibraryFilesRemovalSubmitted, "删除媒体库文件后没有通知外壳刷新页面");
            Require(transport.LastMethod == HttpMethod.Delete && transport.LastPath.EndsWith("deletesrc=true&deletedest=true", StringComparison.Ordinal), "确认未走四档 DELETE 协议");
            await SaveProbeScreenAsync("mp-media-delete-receipt.png");
            InvokeDialog(dialog, "CloseButton");
            await showing;

            transport.Type = "电视剧";
            dialog = new MoviePilotMediaManagementDialog(service, fixture.Session.Capture(), Subject(EmbyItemType.Series), MoviePilotHistoryAction.SmartReorganize) { XamlRoot = _root.XamlRoot };
            showing = dialog.ShowAsync().AsTask();
            await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
            Require(Equals(dialog.Title, "智能批量整理") && transport.Writes == 1, "剧集确认前就提交或措辞不正确");
            await SaveProbeScreenAsync("mp-smart-series-confirm.png");
            InvokeDialog(dialog, "PrimaryButton");
            await UntilAsync(() => transport.Writes == 2 && dialog.PrimaryButtonText.Length == 0);
            Require(transport.LastMethod == HttpMethod.Post && transport.LastPath == "/api/v1/history/transfer/ai-redo" &&
                transport.LastBody == "{\"history_ids\":[2]}", "剧集未使用 batch ai-redo");
            Require(Get<TextBlock>(dialog, "CountText").Text.Contains("不代表文件已整理完成", StringComparison.Ordinal), "任务接收冒充完成");
            await SaveProbeScreenAsync("mp-smart-series-receipt.png");
            InvokeDialog(dialog, "CloseButton");
            await showing;

            transport.AiEnabled = false;
            await service.HistoryCapabilitiesAsync(CancellationToken.None);
            Require(!HasMenuCommand(ItemCommands.Build(fixture.Session, shell, anchor,
                new CardItem(Subject(EmbyItemType.Series), fixture.Images, 170)), ItemCommand.MoviePilotSmartReorganize), "关闭助手后仍给智能整理");
            transport.Manage = false;
            await service.HistoryCapabilitiesAsync(CancellationToken.None);
            Require(!ItemCommands.Build(fixture.Session, shell, anchor,
                new CardItem(Subject(EmbyItemType.Series), fixture.Images, 170)).Items.Any(row => row.Tag is ItemCommand.MoviePilotMediaManagement), "失去权限后仍给媒体管理");
            transport.Manage = true;
            transport.AiEnabled = true;
            await service.HistoryCapabilitiesAsync(CancellationToken.None);
            Require(HasMenuCommand(ItemCommands.Build(fixture.Session, shell, anchor,
                new CardItem(Subject(EmbyItemType.Series), fixture.Images, 170)), ItemCommand.MoviePilotSmartReorganize), "权限恢复后入口未恢复");
            var libraryMenu = ItemCommands.Build(fixture.Session, shell, anchor,
                new CardItem(new EmbyItem { Id = "library", Type = EmbyItemType.CollectionFolder }, fixture.Images, 170));
            Require(!libraryMenu.Items.Any(row => row.Tag is ItemCommand cmd && ItemMenu.IsMoviePilot(cmd)), "媒体库菜单误加 MP 操作");
        }
        finally
        {
            menu?.Hide();
            dialog?.Hide();
            ThemeHost.Apply(originalTheme);
            _window!.TopMost = topMost;
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(anchor);
            _root.Children.Remove(shell);
        }

        static bool HasMenuCommand(MenuFlyout menu, ItemCommand wanted) =>
            menu.Items.Any(row => row.Tag is ItemCommand command && command == wanted)
            || menu.Items.OfType<MenuFlyoutSubItem>().Any(row =>
                row.Items.OfType<MenuFlyoutItem>().Any(item => item.Tag is ItemCommand command && command == wanted));

        void AssertNoBrand(FrameworkElement item)
        {
            if (item is Control control) control.ApplyTemplate();
            Require(!Descendants(item).OfType<Viewbox>().Any(mark => mark.Name == "MoviePilotBrandMark"),
                "条目菜单仍套用 MP 品牌模板");
            Require(AutomationProperties.GetAutomationId(item).Length > 0, "菜单控件没有自动化句柄");
        }

        static void InvokeDialog(ContentDialog target, string name)
        {
            var text = name == "PrimaryButton" ? target.PrimaryButtonText : target.CloseButtonText;
            var button = Descendants(target).OfType<Button>().Single(button => button.Name == name &&
                Equals(button.Content, text) && !new ButtonAutomationPeer(button).IsOffscreen());
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
    }

    /// <summary>专用人工／外部 UIA 检查台：只用匿名 MP，允许实屏工具捕捉原生弹层而非只截宿主像素。</summary>
    private async Task InspectMediaManagementAsync()
    {
        var transport = new MediaManagementProbeServer();
        using var fixture = CreateFixture(transport);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        await service.HistoryCapabilitiesAsync(CancellationToken.None);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/mp-movie", StringComparison.Ordinal)) return Task.FromResult(Json(Subject(EmbyItemType.Movie)));
            if (path.EndsWith("/Items/mp-series", StringComparison.Ordinal)) return Task.FromResult(Json(Subject(EmbyItemType.Series)));
            if (path.EndsWith("/Items", StringComparison.Ordinal) && Query(request, "ParentId") == "mp-series")
                return Task.FromResult(Json(new { Items = new[] { Subject(EmbyItemType.Episode) }, TotalRecordCount = 1 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var shell = new ShellPage { Visibility = Visibility.Collapsed };
        shell.Attach(fixture.Services);
        var layout = new Grid { Padding = new Thickness(24), RowSpacing = 24 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var controls = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock { Text = "隔离测试：仅假服务器，打开／取消确认不写；确认按钮只请求匿名传输。", TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(status, 1);
        layout.Children.Add(controls);
        layout.Children.Add(status);
        _root.Children.Add(shell);
        _root.Children.Add(layout);
        var done = Pending<bool>();
        MenuFlyout? menu = null;
        ContentDialog? dialog = null;
        try
        {
            void Add(string id, string text, Action<Button> action)
            {
                var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
                AutomationProperties.SetAutomationId(button, id);
                button.Click += (_, _) => action(button);
                controls.Children.Add(button);
            }
            void More(Button anchor, string type)
            {
                menu?.Hide();
                var item = type == "library" ? new EmbyItem { Id = "library", Type = EmbyItemType.CollectionFolder }
                    : Subject(type);
                transport.Type = type == EmbyItemType.Series ? "电视剧" : "电影";
                menu = ItemCommands.Build(fixture.Session, shell, anchor, new CardItem(item, fixture.Images, 170));
                menu.ShowAt(anchor);
                status.Text = "已打开真实更多菜单；写请求计数 " + transport.Writes;
            }
            async Task Confirm(string type, MoviePilotHistoryAction action)
            {
                if (dialog is not null) return;
                transport.Type = type == EmbyItemType.Series ? "电视剧" : "电影";
                dialog = new MoviePilotMediaManagementDialog(service, fixture.Session.Capture(), Subject(type), action) { XamlRoot = _root.XamlRoot };
                try { await dialog.ShowAsync().AsTask(); }
                finally { dialog = null; status.Text = "写请求计数 " + transport.Writes + "；最后请求 " + transport.LastMethod + " " + transport.LastPath; }
            }
            Add("MpInspectMovieMenu", "电影封面更多", button => More(button, EmbyItemType.Movie));
            Add("MpInspectSeriesMenu", "剧集封面更多", button => More(button, EmbyItemType.Series));
            Add("MpInspectLibraryMenu", "媒体库更多", button => More(button, "library"));
            Add("MpInspectDeleteConfirm", "媒体删除确认", button => _ = Confirm(EmbyItemType.Movie, MoviePilotHistoryAction.DeleteAll));
            Add("MpInspectSmartConfirm", "智能批量确认", button => _ = Confirm(EmbyItemType.Series, MoviePilotHistoryAction.SmartReorganize));
            foreach (var theme in Momoka.Theming.UiThemes.All)
                Add("MpInspectTheme" + theme.Id, theme.Name, _ => ThemeHost.Apply(theme.Id));
            Add("MpInspectNarrow", "窄窗口", _ => ResizeInspect(760, 900));
            Add("MpInspectWide", "宽窗口", _ => ResizeInspect(1280, 900));
            Add("MpInspectDone", "结束检查", _ => done.TrySetResult(true));
            await LayoutAsync(layout);
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "mp-media-inspect-ready.txt"), Environment.ProcessId.ToString());
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(8)));
        }
        finally
        {
            dialog?.Hide();
            menu?.Hide();
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            _root.Children.Remove(layout);
        }
    }

    private static EmbyItem Subject(string type) => new()
    {
        Id = type == EmbyItemType.Series ? "mp-series" : type == EmbyItemType.Movie ? "mp-movie" : "mp-episode",
        Name = type == EmbyItemType.Series ? "离线测试剧集" : type == EmbyItemType.Episode ? "第 1 集" : "离线测试电影",
        Type = type,
        Path = type == EmbyItemType.Series ? null : type == EmbyItemType.Movie ? "/library/movie.mkv" : "/library/series.mkv",
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" },
        SeriesId = type == EmbyItemType.Episode ? "mp-series" : null,
        SeriesName = "离线测试剧集",
        IndexNumber = 1,
        ParentIndexNumber = 1
    };

    private sealed class MediaManagementProbeServer : HttpMessageHandler
    {
        public string Type { get; set; } = "电影";
        public bool Manage { get; set; } = true;
        public bool AiEnabled { get; set; } = true;
        public int Writes { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string LastPath { get; private set; } = "";
        public string LastBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "媒体管理探针拒绝真实 MP 网络");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture-token" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
            if (path.EndsWith("user/current", StringComparison.Ordinal)) return Json(new { is_active = true, is_superuser = Manage });
            if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = AiEnabled });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Get)
            {
                var id = Type == "电影" ? 1 : 2;
                var name = Type == "电影" ? "movie" : "series";
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        total = 1,
                        list = new[] { new
                {
                    id, title = Type == "电影" ? "离线测试电影" : "离线测试剧集", type = Type, status = true, mode = "softlink",
                    media_source = "themoviedb", media_id = "1", src = $"/downloads/{name}.mkv", dest = $"/library/{name}.mkv",
                    src_fileitem = new { storage = "local", type = "file", path = $"/downloads/{name}.mkv" },
                    dest_fileitem = new { storage = "local", type = "file", path = $"/library/{name}.mkv" }
                } }
                    }
                });
            }
            if (path.StartsWith("/api/v1/history/transfer", StringComparison.Ordinal) && request.Method != HttpMethod.Get)
            {
                Writes++;
                LastMethod = request.Method;
                LastPath = request.RequestUri.PathAndQuery;
                LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
                if (request.Method == HttpMethod.Post) return Json(new { success = true, data = new { progress_key = "fixture-progress", history_ids = new[] { 2 } } });
                return Json(new { success = true, data = new { source = new { status = "deleted" }, destination = new { status = "deleted" }, history = "deleted" } });
            }
            if (path.EndsWith("/subscribe/", StringComparison.Ordinal)) return Json(Array.Empty<object>());
            return Json(Array.Empty<object>());
        }
    }
}
