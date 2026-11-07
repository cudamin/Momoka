using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DanmakuSendAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        fixture.Settings.Danmaku.Enabled = true;
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "danmaku-send"));
        paths.EnsureCreated();
        using var transport = new DanmakuSendTransport();
        using var client = new DandanPlayClient(fixture.Settings, Path.Combine(paths.Root, "cache"), transport,
            credentials: new DanmakuProbeCredentials("offline-app", "offline-secret"));
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings, () => throw new InvalidOperationException("禁止实际播放"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images, shaders, fixture.Dispatcher, client);
        var handle = new DanmakuProbeHandle();
        Field(playback, "_current", handle); // 只模拟在播身份，永远不创建媒体/网络后端。
        vm.Connect();
        vm.Title = "葬送的芙莉莲 · 第 1 话 · 这是一个较长的测试标题";
        typeof(PlayerViewModel).GetProperty("Status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(vm, new PlayerStatus { Position = 100, Duration = 1200, Loaded = true });
        var page = new PlayerPage();
        var shell = new ShellPage();
        page.Attach(vm, shell, _window!);
        page.Visibility = Visibility.Visible;
        _root.Children.Add(page);
        var titleStrip = Get<Grid>(page, "TitleStrip");
        var input = Get<TextBox>(page, "DanmakuInputBox");
        var bar = Get<StackPanel>(page, "DanmakuInputBar");
        var button = Get<Button>(page, "SendDanmakuButton");
        var originalTitle = Get<Border>(page, "TitleBox");
        var windows = Get<FrameworkElement>(page, "WindowButtons");
        try
        {
            await PlayerDanmakuControlsAsync(page, vm, fixture.Settings);
            var item = new EmbyItem { Type = EmbyItemType.Movie, Name = "测试作品" };
            await (Task)Call(vm, "AutoMatchDanmakuAsync", item)!;
            Require(vm.DanmakuStatus.Contains("搜索结果过多", StringComparison.Ordinal) && transport.GetComments == 0,
                "不完整结果未提示细化或自动取了弹幕");
            var result = await vm.SearchDanmakuAsync("测试作品", "1");
            Require(result.Episodes.Count == 1 && result.HasMore, "手选结果被截断状态丢弃");
            await vm.LoadDanmakuAsync(result.Episodes[0]);
            Require(vm.DanmakuCanSend, "手选加载后未允许发送");
            Field(vm, "_danmakuDelay", 10d);
            foreach (var width in new[] { 1000, 760, 360 })
            {
                ResizeProbeClient(width, 720);
                await LayoutAsync(page);
                Call(page, "ArrangeDanmakuInput");
                titleStrip.Visibility = Visibility.Visible;
                titleStrip.Opacity = 1;
                await LayoutAsync(page);
                var a = Bounds(originalTitle);
                var b = Bounds(bar);
                var c = Bounds(windows);
                File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "danmaku-input-layout.txt"), $"{width}: title={a}; input={b}; windows={c}\n");
                Require(b.Left >= a.Right - 1 && b.Right <= c.Left + 1, $"{width}输入栏未位于标题和窗口按钮之间：title={a}; input={b}; windows={c}");
                Require(input.ActualWidth >= 40 && button.ActualWidth >= 40, $"{width}输入栏或发送按钮被挤没");
                input.Text = "中文弹幕 空格 D";
                Require(vm.DanmakuDraft == input.Text, "输入框未即时绑定草稿");
                input.Focus(FocusState.Programmatic);
                await LayoutAsync(input);
                Require((bool)typeof(PlayerPage).GetProperty("DanmakuInputFocused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!, "输入焦点未被识别");
                Require(!((Momoka.Shell.Windowing.IWin32KeySink)page).WantsKey((int)Windows.System.VirtualKey.Space), "输入中空格仍被播放器兜底接管");
                var inputBorder = Descendants(input).OfType<Border>().Single(element => element.Name == "BorderElement");
                Require(ReferenceEquals(inputBorder.Background, page.Resources["PlayerGlassTopBrush"]),
                    $"{width}播放器玻璃输入框聚焦后被普通表单底色盖住");
                await SaveProbeScreenAsync($"danmaku-input-{width}-screen.png");
                fixture.Settings.Danmaku.ShowInput = false;
                Call(vm, "RefreshDanmakuProperties");
                await LayoutAsync(page);
                Require(bar.Visibility == Visibility.Collapsed && fixture.Settings.Danmaku.Show && vm.DanmakuDraft == "中文弹幕 空格 D", "独立关闭输入栏影响了弹幕或草稿");
                await SaveProbeScreenAsync($"danmaku-input-hidden-{width}-screen.png");
                fixture.Settings.Danmaku.ShowInput = true;
                fixture.Settings.Danmaku.Show = false;
                Call(vm, "RefreshDanmakuProperties");
                await LayoutAsync(page);
                Require(bar.Visibility == Visibility.Collapsed, "关闭弹幕未隐藏输入栏");
                fixture.Settings.Danmaku.Show = true;
                Call(vm, "RefreshDanmakuProperties");
                await LayoutAsync(page);
            }
            transport.Pending = Pending<HttpResponseMessage>();
            var invocation = new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Require(invocation is not null, "发送按钮没有UIA调用入口");
            invocation!.Invoke();
            await UntilAsync(() => transport.Posts == 1);
            Require(vm.DanmakuSending && !button.IsEnabled, "发送中未阻止重复提交");
            await vm.SendDanmakuCommand.ExecuteAsync(null);
            Require(transport.Posts == 1, "并发发送重复写入");
            input.Text = "下一条草稿";
            fixture.Settings.Danmaku.ShowInput = false;
            Call(vm, "RefreshDanmakuProperties");
            transport.Pending.SetResult(Json(new { success = true, errorCode = 0, cid = 12L }));
            await UntilAsync(() => !vm.DanmakuSending);
            Require(vm.DanmakuSendStatus == "已发送" && vm.DanmakuDraft == "下一条草稿", "隐藏后丢失已确认结果或清掉新草稿");
            Require(transport.Time == 90, "发送时间未减去10秒弹幕延迟");
            transport.Pending = null;
            fixture.Settings.Danmaku.ShowInput = true;
            Call(vm, "RefreshDanmakuProperties");
            await vm.SendDanmakuCommand.ExecuteAsync(null);
            Require(vm.DanmakuDraft.Length == 0 && transport.Posts == 2, "成功没有清除已发草稿");
            transport.Fail = true;
            vm.DanmakuDraft = "失败保留";
            await vm.SendDanmakuCommand.ExecuteAsync(null);
            Require(vm.DanmakuDraft == "失败保留" && vm.DanmakuSendStatus.Contains("未接受", StringComparison.Ordinal), "失败未保留输入及错误");
            transport.Fail = false;
            transport.Pending = Pending<HttpResponseMessage>();
            var sending = vm.SendDanmakuCommand.ExecuteAsync(null);
            await UntilAsync(() => transport.Posts == 4);
            Call(vm, "ResetDanmakuInput");
            vm.DanmakuDraft = "新影片草稿";
            transport.Pending.SetResult(Json(new { success = true, errorCode = 0, cid = 14L }));
            await sending;
            Require(vm.DanmakuDraft == "新影片草稿" && vm.DanmakuSendStatus.Length == 0, "旧片迟到回执污染新片");
            transport.Fail = true;
            await vm.LoadDanmakuAsync(new(230002, "另一个作品", "第2话"));
            Require(!vm.DanmakuCanSend && button.Visibility == Visibility.Collapsed, "换库加载失败仍显示可发送");
        }
        finally
        {
            Field(playback, "_current", null);
            page.Detach();
            page.Shutdown();
            _root.Children.Remove(page);
            page.ReleaseVideoSurface();
            await vm.ShutdownAsync();
        }

        Rect Bounds(FrameworkElement element)
        {
            var point = element.TransformToVisual(page).TransformPoint(new Point());
            return new(point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }
    }

    private async Task PlayerDanmakuControlsAsync(PlayerPage page, PlayerViewModel vm, AppSettings settings)
    {
        var panelButton = Get<Button>(page, "DanmakuButton");
        var toggle = Descendants(page).OfType<Button>().SingleOrDefault(control => control.Name == "DanmakuToggleButton")
            ?? throw new InvalidOperationException("播放页缺少独立的一键弹幕开关");
        var input = Get<StackPanel>(page, "DanmakuInputBar");
        var pictureMenu = Get<MenuFlyout>(page, "PictureMenu");
        var opacity = settings.Danmaku.Opacity;
        var layout = new List<string>();
        foreach (var width in new[] { 1100, 760, 420, 360 })
        {
            ResizeProbeClient(width, 720);
            await LayoutAsync(page);
            page.ShowChromeForShot("playing");
            Call(page, "ArrangeTransport");
            await LayoutAsync(page);
            Require(panelButton.Visibility == Visibility.Visible && toggle.Visibility == Visibility.Visible,
                $"{width}: 启用弹幕后面板与一键按钮未同时显示");
            var controls = new FrameworkElement[] { Get<FrameworkElement>(page, "PlayButton"),
                Get<FrameworkElement>(page, "TransportStatsButton"), panelButton, toggle,
                Get<FrameworkElement>(page, "SpeedStrip"), Get<FrameworkElement>(page, "SpeedButton"),
                Get<FrameworkElement>(page, "SubtitleButton"), Get<FrameworkElement>(page, "AudioButton"),
                Get<FrameworkElement>(page, "FullscreenButton") };
            Rect? previous = null;
            foreach (var control in controls.Where(control => control.Visibility == Visibility.Visible))
            {
                var bounds = Bounds(control);
                Require(bounds.Left >= -1 && bounds.Right <= width + 1 && bounds.Width > 0,
                    $"{width}: {control.Name}越界或无尺寸");
                Require(previous is not { } before || before.Right <= bounds.Left + 1,
                    $"{width}: {control.Name}与前一按钮重叠");
                previous = bounds;
            }
            Require(Math.Abs(toggle.ActualWidth - panelButton.ActualWidth) < 1
                && Math.Abs(toggle.ActualHeight - panelButton.ActualHeight) < 1, "弹幕两颗按钮缩放不同步");
            Require(AutomationProperties.GetName(toggle) == "关闭弹幕", "开启状态读屏操作不明确");
            Require(ReferenceEquals(toggle.Background, page.Resources["PlayerStripHoverBrush"]), "开启状态没有高亮");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(page);
            Require(!settings.Danmaku.Show && toggle.Visibility == Visibility.Visible && input.Visibility == Visibility.Collapsed,
                "一键关闭未停止显示，或把恢复按钮一起隐藏");
            Require(AutomationProperties.GetName(toggle) == "开启弹幕"
                && !ReferenceEquals(toggle.Background, page.Resources["PlayerStripHoverBrush"]), "关闭状态提示或高亮未同步");
            Require(Get<object?>(page, "_danmakuPanel") is null, "一键开关打开了搜索面板");
            await SaveProbeScreenAsync($"player-danmaku-toggle-off-{width}-screen.png");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(page);
            Require(settings.Danmaku.Show && input.Visibility == Visibility.Visible, "一键恢复未更新弹幕或输入栏");
            await SaveProbeScreenAsync($"player-danmaku-toggle-on-{width}-screen.png");
            layout.Add($"{width}: panel={Bounds(panelButton)}; toggle={Bounds(toggle)}; state={settings.Danmaku.Show}");
        }

        Call(page, "OnPictureMenuOpening", page, new object());
        Require(MenuDanmakuRows().Count == 2, "启用后右键缺少面板或弹幕设置入口");
        var settingsGroup = pictureMenu.Items.OfType<MenuFlyoutSubItem>().Single(group => group.Text == PlayerSettingsLinks.MenuLabel);
        var staleSettingsRow = settingsGroup.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is PlayerSettingsLink { Token: "danmaku" });
        var settingsRequests = 0;
        void Requested(PlayerSettingsLink link) { if (link.Token == "danmaku") settingsRequests++; }
        vm.SettingsRequested += Requested;
        try
        {
            pictureMenu.ShowAt(page);
            await LayoutAsync(page);
            settings.Danmaku.Enabled = false;
            ShellPrefs.ApplyDanmaku();
            await LayoutAsync(page);
            Require(!pictureMenu.IsOpen, "关闭全局弹幕后旧右键菜单仍打开");
            Require(panelButton.Visibility == Visibility.Collapsed && toggle.Visibility == Visibility.Collapsed
                && input.Visibility == Visibility.Collapsed, "关闭全局弹幕后仍有弹幕播放控件");
            Call(page, "OnPictureMenuOpening", page, new object());
            Require(MenuDanmakuRows().Count == 0, "关闭全局弹幕后右键仍泄漏相关入口");
            Call(page, "OnPictureSettingsRow", staleSettingsRow, new RoutedEventArgs());
            Call(page, "OnDanmakuToggleClick", toggle, new RoutedEventArgs());
            Require(settingsRequests == 0 && settings.Danmaku.Show && settings.Danmaku.Opacity == opacity,
                "旧弹幕入口仍执行或隐藏清空了偏好");
            await SaveProbeScreenAsync("player-danmaku-disabled-screen.png");
            settings.Danmaku.Enabled = true;
            ShellPrefs.ApplyDanmaku();
            await LayoutAsync(page);
            Call(page, "OnPictureMenuOpening", page, new object());
            Require(toggle.Visibility == Visibility.Visible && MenuDanmakuRows().Count == 2, "重新启用未恢复播放入口");

            settings.Danmaku.Source = DanmakuSourceMode.DanmuApi;
            settings.Danmaku.DanmuApiBaseUrl = "";
            ShellPrefs.ApplyDanmaku();
            await LayoutAsync(page);
            Call(page, "OnPictureMenuOpening", page, new object());
            Require(toggle.Visibility == Visibility.Collapsed && MenuDanmakuRows().Count == 0,
                "来源地址未配置仍显示弹幕操作");
            settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
            settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            ShellPrefs.ApplyDanmaku();
            await LayoutAsync(page);
            Call(page, "OnPictureMenuOpening", page, new object());
            Require(toggle.Visibility == Visibility.Collapsed && MenuDanmakuRows().Count == 0, "外部后端仍显示弹幕操作");
            settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
            ShellPrefs.ApplyDanmaku();
            await LayoutAsync(page);
            Call(page, "OnPictureMenuOpening", page, new object());
            Require(toggle.Visibility == Visibility.Visible && MenuDanmakuRows().Count == 2
                && settings.Danmaku.Opacity == opacity && settings.Danmaku.Show, "恢复内置后端未恢复原值与入口");
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "danmaku-toggle-layout.txt"), layout);
        }
        finally
        {
            vm.SettingsRequested -= Requested;
            pictureMenu.Hide();
        }

        List<MenuFlyoutItem> MenuDanmakuRows() => pictureMenu.Items.OfType<MenuFlyoutItem>()
            .Where(row => row.Text.Contains("弹幕", StringComparison.Ordinal)).Concat(
                pictureMenu.Items.OfType<MenuFlyoutSubItem>().SelectMany(group => group.Items.OfType<MenuFlyoutItem>())
                    .Where(row => row.Tag is PlayerSettingsLink { Token: "danmaku" })).ToList();

        Rect Bounds(FrameworkElement element)
        {
            var point = element.TransformToVisual(page).TransformPoint(new Point());
            return new(point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }
    }

    private sealed class DanmakuSendTransport : HttpMessageHandler
    {
        public int Posts { get; private set; }
        public int GetComments { get; private set; }
        public double Time { get; private set; }
        public bool Fail { get; set; }
        public TaskCompletionSource<HttpResponseMessage>? Pending { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Time = body.RootElement.GetProperty("time").GetDouble();
                if (Pending is { } pending) return await pending.Task; // 故意模拟取消之后仍确认成功。
                return Json(new { success = !Fail, errorCode = Fail ? 1 : 0, cid = 13 });
            }
            if (request.RequestUri!.AbsolutePath.Contains("/search/", StringComparison.Ordinal))
                return Json(new
                {
                    success = true,
                    errorCode = 0,
                    hasMore = true,
                    animes = new[] { new { animeTitle = "测试作品", episodes = new[] { new { episodeId = 230001, episodeTitle = "第1话" } } } },
                });
            GetComments++;
            return Fail ? Json(new { success = false, errorCode = 1 }) : Json(new { comments = Array.Empty<object>() });
        }
    }

    private sealed class DanmakuProbeHandle : IPlaybackHandle
    {
        public bool HasControlChannel => false;
        public bool? PictureInHostWindow => true;
        public bool IsPaused => false;
        public event Action<bool>? PauseChanged { add { } remove { } }
        public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult<double?>(100);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("禁止播放");
        public Task StopAsync() => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(null);
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
