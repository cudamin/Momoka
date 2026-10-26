using System.Globalization;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private readonly List<Window> _shortcutProbeWindows = [];

    private async Task SettingsShortcutSearchAsync(int width)
    {
        using var fixture = CreateFixture();
        fixture.Settings.Shortcuts.Bindings["toggle-pause"] = "Ctrl+F9";
        fixture.Settings.Shortcuts.Bindings["video-pan-left"] = "";
        fixture.Settings.Danmaku.Enabled = true;
        fixture.Settings.Danmaku.AppId = "offline-app";
        fixture.Settings.Danmaku.ProtectedAppSecret = "offline-secret";
        fixture.Settings.Danmaku.CredentialSource = DanmakuCredentialSource.Custom;
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"shortcut-search-{width}"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        await page.ViewModel.ReloadAsync();
        page.SelectedCategory = "快捷键";
        // 正式设置住在框架 Window，而不是主播放器的 XAML 岛；验证同一种窗口的呈现生命周期。
        var content = new Grid();
        content.Children.Add(page);
        ThemeHost.Register(content);
        var settingsWindow = new Window { Content = content, Title = string.Empty };
        _shortcutProbeWindows.Add(settingsWindow); // 最后一个框架窗口关闭会终止应用，整轮结束后统一关闭。
        var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(settingsWindow.AppWindow.Id);
        var ownerWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(ownerWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        // 检查窗摆在所选屏幕左上工作区，不沿用主窗居中偏移；窄窗才不会跨进用户的置顶聊天窗口。
        settingsWindow.AppWindow.Move(new Windows.Graphics.PointInt32(workArea.X + 24, workArea.Y + 24));
        settingsWindow.Activate();
        settingsWindow.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
        var presenter = settingsWindow.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter
            ?? throw new InvalidOperationException("设置探针窗口不是可核对置顶状态的普通窗口");
        var wasTopMost = presenter.IsAlwaysOnTop;
        presenter.IsAlwaysOnTop = true;
        try
        {
            BeginSettingsFixtureVisit(page);
            await LayoutAsync(page);
            // WinUI 的第二扇 Window 初始化会先送一次紧凑内容尺寸；等首帧后提交目标客户区，
            // 不能把仅有导航行的临时 154px 画面当作 900px 窄窗截图。
            await WaitForDiagnosticsFrameAsync();
            settingsWindow.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
            await UntilAsync(() => Momoka.Shell.Interop.Native.GetClientRect(handle, out var rect)
                && Math.Abs(rect.Width - width) < 2 && Math.Abs(rect.Height - 900) < 2);
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var card = page.ViewModel.Sections.Single(section => section.Category == "快捷键");
            var originalRows = card.Rows.OfType<SettingShortcutRow>().ToDictionary(row => row.Id);
            var source = card.Rows;
            var search = Get<AutoSuggestBox>(page, "ShortcutSearchBox");
            var clear = Get<Button>(page, "ClearShortcutSearchButton");
            var input = Descendants(search).OfType<TextBox>().Single();
            Require(originalRows.Count == ShortcutCatalog.Actions.Count, "初始快捷键目录不完整");
            Require(search.ActualWidth > 200 && clear.Visibility == Visibility.Collapsed, "搜索框过窄或空查询仍显示清空按钮");
            await SaveShortcutScreenAsync($"settings-shortcut-search-all-{width}-screen.png");

            await QueryAsync("字幕");
            Require(Shown().Any() && Shown().All(row => row.Label.Contains("字幕", StringComparison.Ordinal)), "操作名过滤不准确");
            Require(ReferenceEquals(source, card.Rows) && Shown().All(row => ReferenceEquals(row, originalRows[row.Id])), "搜索重建了行或 ItemsSource");
            Require(clear.Visibility == Visibility.Visible && page.ViewModel.ShortcutSearchSummary.StartsWith("找到 ", StringComparison.Ordinal), "查询回执或清空入口缺失");
            await SaveShortcutScreenAsync($"settings-shortcut-search-subtitle-{width}-screen.png");

            await QueryAsync("ctrl + f9");
            Require(Shown().Select(row => row.Id).SequenceEqual(new[] { "toggle-pause" }), "没有按当前自定义组合键过滤");
            var capture = Descendants(page).OfType<ShortcutCaptureBox>().Single(box => AutomationProperties.GetName(box) == "播放 / 暂停");
            originalRows["toggle-pause"].OnCommitted(capture, "Ctrl+F8");
            await LayoutAsync(page);
            Require(!Shown().Any(), "改绑后旧键仍留在搜索结果");
            await QueryAsync("ctrl+f8");
            Require(Shown().Single().ComboText == "Ctrl + F8", "改绑后新键没有立即可搜索");
            await QueryAsync("空格");
            Require(!Shown().Any(), "已改绑的动作仍能按原默认键找到");

            await QueryAsync("Alt + [");
            Require(Shown().Single().Id == "subtitle-scale-down", "方括号组合键搜索失败");
            await QueryAsync("未设置");
            Require(Shown().Single().Id == "video-pan-left", "未设置搜索未显示解绑动作");
            await QueryAsync("Esc");
            Require(!Shown().Any() && card.Rows.OfType<SettingFactRow>().Any(row => row.Label == "退出全屏 / 停止"), "固定键没有参与搜索");
            await QueryAsync("zzzz-not-a-shortcut");
            Require(!Shown().Any() && card.Rows.Any(row => row.Label == "没有找到匹配的快捷键"), "空结果提示缺失");
            Require(card.Rows.Any(row => row.Label == "恢复默认快捷键"), "搜索结果为空时丢了恢复默认入口");
            await SaveShortcutScreenAsync($"settings-shortcut-search-empty-{width}-screen.png");
            var emptyText = Descendants(page).OfType<TextBlock>().Single(text => text.Text == "没有找到匹配的快捷键");
            var shownEmptyPixels = EmptyPixels();
            var rejectedInvalidWindow = false;
            try { CompositionPlaybackProbe.BrightPixels(IntPtr.Zero, new Windows.Foundation.Rect(0, 0, 1, 1)); }
            catch (InvalidOperationException) { rejectedInvalidWindow = true; }
            Require(rejectedInvalidWindow, "屏幕采样没有拒绝无效窗口句柄");
            var hiddenEmptyPixels = -1;
            emptyText.Opacity = 0;
            try
            {
                await SaveReturnFrameAsync(page, $"settings-shortcut-search-empty-hidden-{width}-render.png");
                await SettleShortcutPictureAsync();
                await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory,
                    $"settings-shortcut-search-empty-hidden-{width}-screen.png"));
                hiddenEmptyPixels = EmptyPixels();
            }
            finally { emptyText.Opacity = 1; }

            await QueryAsync("弹幕");
            Require(Shown().Count() == 4, "新增弹幕归零动作缺失");
            fixture.Settings.Danmaku.Enabled = false;
            page.ViewModel.OnDanmakuPreferencesChanged();
            await LayoutAsync(page);
            Require(!Shown().Any() && search.Text == "弹幕", "隐藏无效弹幕动作时丢失搜索词");
            fixture.Settings.Danmaku.Enabled = true;
            page.ViewModel.OnDanmakuPreferencesChanged();
            await LayoutAsync(page);
            Require(Shown().Count() == 4 && fixture.Settings.Shortcuts.Bindings["toggle-pause"] == "Ctrl+F8", "恢复可用性丢了动作或改绑偏好");
            page.SelectedCategory = "播放行为";
            await LayoutAsync(page);
            Require(page.ViewModel.ShortcutSearchVisibility == Visibility.Collapsed, "快捷键搜索漏到其他分类");
            page.SelectedCategory = "快捷键";
            await LayoutAsync(page);
            Require(search.Text == "弹幕" && Shown().Count() == 4, "切换分类后搜索状态没有保留");

            var reset = card.Rows.OfType<SettingFactRow>().Single(row => row.Label == "恢复默认快捷键");
            reset.RunCommand.Execute(null);
            await LayoutAsync(page);
            Require(fixture.Settings.Shortcuts.Bindings.Count == 0, "搜索中恢复默认只清了可见动作");
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(page);
            Require(search.Text.Length == 0 && Shown().Count() == ShortcutCatalog.Actions.Count, "清空搜索没有恢复完整列表");
            Require(ReferenceEquals(source, card.Rows), "恢复默认替换了稳定集合");
            await SaveShortcutScreenAsync($"settings-shortcut-search-restored-{width}-screen.png");
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, $"settings-shortcut-search-{width}-evidence.txt"),
                $"逻辑：名称／当前键／改绑／解绑／空态／固定键／能力隐藏恢复／分类保留／全部恢复／清空均通过。\n"
                + $"实屏空结果文字：显示 {shownEmptyPixels} 亮像素，隐藏 {hiddenEmptyPixels} 亮像素。\n");
            Require(shownEmptyPixels > 20, "空结果文字没有实际画到屏幕，不能把旧卡片截图当成快捷键页；控件逻辑已全部检查，视觉闸门仍未通过");
            Require(hiddenEmptyPixels == 0, "屏幕检查无法识别故意隐藏的空结果文字");

            int EmptyPixels()
            {
                var bounds = emptyText.TransformToVisual(content).TransformBounds(new Windows.Foundation.Rect(0, 0, emptyText.ActualWidth, emptyText.ActualHeight));
                var scale = content.XamlRoot.RasterizationScale;
                return CompositionPlaybackProbe.BrightPixels(handle, new Windows.Foundation.Rect(bounds.X * scale, bounds.Y * scale,
                    bounds.Width * scale, bounds.Height * scale));
            }
            async Task SettleShortcutPictureAsync()
            {
                content.UpdateLayout();
                await WaitForSettingsSettledAsync(page);
                var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var frames = 0;
                void OnRendering(object? sender, object args)
                {
                    if (++frames >= 2) rendered.TrySetResult();
                }
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
                try { await rendered.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
                finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering; }
            }
            async Task SaveShortcutScreenAsync(string name)
            {
                // 先完成当前树的合成渲染，再采实屏；倒过来会拍到上一轮树的缓存帧。
                // 渲染图与实屏分开保存，实际文字显示／隐藏仍由屏幕像素闸门核对。
                await SaveReturnFrameAsync(page, name.Replace("-screen.png", "-render.png", StringComparison.Ordinal));
                await SettleShortcutPictureAsync();
                settingsWindow.Activate();
                // 激活后再次提升本轮自己的窗口：系统可能让刚激活的非置顶主窗挡住新建的紧凑窗口。
                if (!Momoka.Shell.Interop.Native.SetWindowPos(handle, Momoka.Shell.Interop.Native.HwndTopMost,
                    0, 0, 0, 0, Momoka.Shell.Interop.Native.SwpNoMove | Momoka.Shell.Interop.Native.SwpNoSize | Momoka.Shell.Interop.Native.SwpNoActivate))
                    throw new System.ComponentModel.Win32Exception();
                await Task.Delay(80);
                Momoka.Shell.Interop.Native.GetClientRect(handle, out var client);
                var origin = new Momoka.Shell.Interop.NativePoint();
                Momoka.Shell.Interop.Native.ClientToScreen(handle, ref origin);
                var readings = new List<string>();
                foreach (var (x, y) in new[] { (0.05, 0.2), (0.95, 0.2), (0.05, 0.8), (0.95, 0.8) })
                {
                    var at = new Momoka.Shell.Interop.NativePoint { X = origin.X + (int)(client.Width * x), Y = origin.Y + (int)(client.Height * y) };
                    var actual = Momoka.Shell.Interop.Native.GetAncestor(Momoka.Shell.Interop.Native.WindowFromPoint(at), Momoka.Shell.Interop.Native.GaRoot);
                    Momoka.Shell.Interop.Native.GetWindowThreadProcessId(actual, out var owner);
                    var classBuffer = new char[256];
                    var classLength = Momoka.Shell.Interop.Native.GetClassName(actual, classBuffer, classBuffer.Length);
                    readings.Add($"{at.X},{at.Y}=0x{actual:X} pid={owner} class={new string(classBuffer, 0, Math.Max(0, classLength))}");
                }
                File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, name.Replace(".png", "-window.txt", StringComparison.Ordinal)),
                    $"HWND=0x{handle:X}; client={client.Width}x{client.Height}; root={content.ActualWidth}x{content.ActualHeight}; page={page.ActualWidth}x{page.ActualHeight}; " + string.Join("; ", readings));
                await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory, name));
            }
            IEnumerable<SettingShortcutRow> Shown() => card.Rows.OfType<SettingShortcutRow>();
            async Task QueryAsync(string text)
            {
                input.Text = text;
                await LayoutAsync(page);
                Require(page.ViewModel.ShortcutSearchQuery == text, "搜索输入没有经绑定传给 ViewModel");
            }
        }
        finally
        {
            page.Release();
            // 保留框架 Window 以免最后一扇窗关闭退出应用，但不保留旧 XamlRoot 的弹层/焦点。
            if (content.XamlRoot is { } root)
                foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root)) popup.IsOpen = false;
            presenter.IsAlwaysOnTop = wasTopMost;
            settingsWindow.AppWindow.Hide();
            settingsWindow.Content = null;
        }
    }

    private async Task PlayerAdditionalShortcutsAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "player-shortcuts"));
        paths.EnsureCreated();
        using var client = new DandanPlayClient(fixture.Settings, new(PassthroughSecretProtector.Instance), Path.Combine(paths.Root, "cache"), new RejectNetwork());
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings, () => throw new InvalidOperationException("快捷键探针禁止创建真实后端"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images, shaders, fixture.Dispatcher, client);
        var handle = new ShortcutProbeHandle();
        Field(playback, "_current", handle);
        // 模拟完整在播身份，让 SetProperty/音频延迟的生命周期守卫也参与测试；构造不会启动后端或上报。
        var request = new PlaybackRequest { MediaUrl = new Uri("file:///C:/offline/not-played.mp4"), Title = "离线快捷键夹具" };
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var tracksType = typeof(PlaybackService).Assembly.GetType("Momoka.Playback.PlaybackTrackState")!;
        var tracks = Activator.CreateInstance(tracksType, flags, null, [new Momoka.Emby.MediaSource(), request], null)!;
        var attemptType = typeof(PlaybackService).GetNestedType("PlayingAttempt", System.Reflection.BindingFlags.NonPublic)!;
        var attempt = Activator.CreateInstance(attemptType, flags, null, [handle, request, playback.Generation, tracks, fixture.Session.Capture()], null)!;
        Field(playback, "_active", attempt);
        var page = new PlayerPage();
        page.Attach(vm, new ShellPage(), _window!);
        _root.Children.Add(page);
        try
        {
            var mapping = page.ProbeShortcuts();
            Require(mapping.Ok, mapping.Detail);
            var quiet = page.ProbeQuietKeys();
            Require(quiet.Ok, quiet.Detail);
            Require(vm.RunAdditionalShortcut("seek-forward-fine"), "精细跳转没有处理器");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("seek|1|relative+exact"), "精细跳转没有发送实际命令");
            Require(vm.RunAdditionalShortcut("video-pan-reset"), "画面归零没有处理器");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("set|video-pan-x|0") && handle.Commands.Contains("set|video-pan-y|0"), "画面归零没有发送两轴命令");
            Require(vm.RunAdditionalShortcut("speed-double") && vm.SpeedValue == 2, "倍速快捷键没有同步 UI 状态");
            vm.SetDelay(subtitle: true, 1.2);
            Require(vm.RunAdditionalShortcut("subtitle-delay-reset") && vm.SubtitleDelay == 0 && handle.Numbers["sub-delay"] == 0, "字幕归零未同步状态和命令");
            Require(vm.RunAdditionalShortcut("audio-delay-reset"), "音频归零没有处理器");
            await UntilAsync(() => handle.Commands.Contains("set|audio-delay|0"));

            // 拒绝第一轴后继续复位第二轴，最后不能显示成功通知。
            handle.Accept = arguments => !(arguments.Count > 1 && arguments[1] == "video-pan-x");
            handle.Commands.Clear();
            vm.RunAdditionalShortcut("video-pan-reset");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("set|video-pan-y|0") && handle.Commands.Last().Contains("没全部成功", StringComparison.Ordinal), "部分失败被误报为成功");
            handle.Accept = _ => true;

            var pending = Pending<bool>();
            handle.Pending = pending;
            handle.Commands.Clear();
            vm.RunAdditionalShortcut("video-zoom-in");
            vm.RunAdditionalShortcut("clear-ab-loop");
            Field(vm, "_generation", 99);
            pending.SetResult(true);
            await Get<Task>(vm, "_pictureCommandTask");
            Require(!handle.Commands.Any(command => command.Contains("ab-loop", StringComparison.Ordinal)), "队列中的旧快捷键影响了下一场播放");
            Field(playback, "_current", null);
            Require(!vm.RunAdditionalShortcut("toggle-loop"), "没有控制通道仍宣称执行成功");
        }
        finally
        {
            _root.Children.Remove(page);
            Field(playback, "_current", null);
            Field(playback, "_active", null);
            await vm.ShutdownAsync();
        }
    }

    private sealed class ShortcutProbeHandle : IPlaybackHandle, IPlayerControl
    {
        public List<string> Commands { get; } = [];
        public Dictionary<string, double> Numbers { get; } = new(StringComparer.Ordinal);
        public Func<IReadOnlyList<string>, bool> Accept { get; set; } = _ => true;
        public TaskCompletionSource<bool>? Pending { get; set; }
        public bool HasControlChannel => true;
        public bool? PictureInHostWindow => true;
        public bool IsPaused => false;
        public PlayerStatus Status { get; } = new() { Loaded = true, Duration = 1000, Position = 100 };
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult<double?>(100);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("禁止真实播放");
        public Task StopAsync() => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken)
        {
            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, out var number)) Numbers[name] = number;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(Numbers.GetValueOrDefault(name));
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(string.Join('|', arguments));
            if (arguments.Count > 2 && arguments[0] == "set" && double.TryParse(arguments[2], CultureInfo.InvariantCulture, out var number)) Numbers[arguments[1]] = number;
            if (Pending is not { } pending) return Task.FromResult(Accept(arguments));
            Pending = null;
            return pending.Task;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
