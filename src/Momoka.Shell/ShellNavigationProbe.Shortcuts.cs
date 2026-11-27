using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Interop;
using Momoka.Shell.Localization;
using Momoka.Shell.Services;
using Momoka.Translation;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

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
        var settingsWindow = SettingsWindow.CreateCaptionedWindow(content);
        var windowRoot = (Grid)settingsWindow.Content;
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
            await ResizeSettingsProbeWindowAsync(settingsWindow, width, 900);
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var globalSearch = Get<AutoSuggestBox>(page, "SettingsSearchBox");
            var searchBounds = globalSearch.TransformToVisual(windowRoot).TransformBounds(
                new Windows.Foundation.Rect(0, 0, globalSearch.ActualWidth, globalSearch.ActualHeight));
            var clientOrigin = new NativePoint();
            Require(Native.ClientToScreen(handle, ref clientOrigin), "无法读取设置窗口客户区起点");
            Require(Native.GetVisibleFrame(handle, out var windowBounds), "无法读取设置窗口边框");
            var caption = windowRoot.Children.OfType<Border>().Single(child => child.Name == "SettingsWindowTitleBar");
            var captionBounds = caption.TransformToVisual(windowRoot).TransformBounds(
                new Windows.Foundation.Rect(0, 0, caption.ActualWidth, caption.ActualHeight));
            Require(caption.ActualHeight > 0 && searchBounds.Top >= captionBounds.Bottom,
                "搜索框与独立标题栏重叠");
            var scale = content.XamlRoot.RasterizationScale;
            var captionMiddle = clientOrigin.Y + (int)Math.Round((captionBounds.Top + captionBounds.Height / 2) * scale);
            Require(Hit(windowBounds.Left + windowBounds.Width / 2, captionMiddle) == Native.HitCaption,
                "设置窗口顶部未保留可拖动标题栏");
            Require(Hit(windowBounds.Right - (int)Math.Round(20 * scale), captionMiddle) == Native.HitClose,
                "设置窗口关闭按钮被正文覆盖");
            foreach (var x in new[] { searchBounds.Left + 4, searchBounds.Right - 4 })
            {
                var px = clientOrigin.X + (int)Math.Round(x * scale);
                var py = clientOrigin.Y + (int)Math.Round((searchBounds.Top + 4) * scale);
                var hit = Hit(px, py);
                Require(hit == Native.HitClient, $"搜索框被窗口标题栏截获：宽{width}，命中{hit}");
            }
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, $"settings-caption-{width}-evidence.txt"),
                $"标题栏高度={settingsWindow.AppWindow.TitleBar.Height}；客户区顶端={clientOrigin.Y}；窗口顶端={windowBounds.Top}；"
                + $"搜索框={searchBounds}；缩放={scale}；搜索框左右上缘均命中客户区。\n");
            int Hit(int x, int y) => (int)(long)Native.SendMessage(handle, Native.WmNcHitTest, IntPtr.Zero,
                new IntPtr(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
            // 2026-11-24 用户令「给设置中的快捷键页面分区」：这一页不再是一张卡，按动作归组排成十三张；下列检查
            // 一律按「所有分区的行」合计来数，免得把分区变化当成丢行。
            var shortcutCards = page.ViewModel.Sections.Where(section => section.Category == "快捷键").ToList();
            var allRows = shortcutCards.SelectMany(section => section.Rows).ToList();
            var originalRows = allRows.OfType<SettingShortcutRow>().ToDictionary(row => row.Id);
            var sources = shortcutCards.Select(section => (Section: section, Rows: section.Rows)).ToList();
            var search = Get<AutoSuggestBox>(page, "ShortcutSearchBox");
            var clear = Get<Button>(page, "ClearShortcutSearchButton");
            var input = Descendants(search).OfType<TextBox>().Single();
            Require(originalRows.Count == ShortcutCatalog.Actions.Count, "初始快捷键目录不完整");
            Require(allRows.Count == originalRows.Count + 7, "可改动作之外应只有六行附注和恢复入口");
            Require(shortcutCards.Count == 13, $"快捷键页应当分十三张卡，实际 {shortcutCards.Count} 张");
            Require(originalRows[ShortcutCatalog.ExitPlayback].ComboText == "Esc", "退出全屏／退出播放的可改行缺失或默认键不对");
            Require(originalRows[ShortcutCatalog.DismissSkip].ComboText == "Del", "关闭跳过提示的可改行缺失或默认键不对");
            Require(originalRows[ShortcutCatalog.TakeSkip].ComboText == "回车", "确认跳过的可改行缺失或默认键不对");
            // 四颗方向键的步长紧跟各自的快捷键行，且缩进一档（从属行）；数值仍是 PlaybackSettings 的那四个。
            foreach (var (actionId, label, seconds) in new[]
            {
                ("seek-backward", "快退跨度（秒）", fixture.Settings.Playback.SeekBackwardSeconds),
                ("seek-forward", "快进跨度（秒）", fixture.Settings.Playback.SeekForwardSeconds),
                ("seek-backward-long", "大步快退跨度（秒）", fixture.Settings.Playback.SeekBackwardLongSeconds),
                ("seek-forward-long", "大步快进跨度（秒）", fixture.Settings.Playback.SeekForwardLongSeconds)
            })
            {
                var index = allRows.FindIndex(row => row is SettingShortcutRow shortcut && shortcut.Id == actionId);
                Require(index >= 0, $"{label} 上面那颗快捷键行缺失");
                Require(index + 1 < allRows.Count && allRows[index + 1] is SettingNumberRow span
                    && span.Label == label && span.IsSubordinate && span.Value == seconds,
                    $"{label} 没有紧跟在 {actionId} 下面，或不是从属行");
            }
            Require(search.ActualWidth > 200 && clear.Visibility == Visibility.Collapsed, "搜索框过窄或空查询仍显示清空按钮");
            // 2026-11-24 用户令「在快退和快进下方新增，快进跨度（倍），长按箭头以这个倍速播放」、随后「快退也要」：
            // 快退与快进各挂一行倍率，紧跟各自的「跨度（秒）」，仍是从属行，范围 2～10，值来自 PlaybackSettings。
            foreach (var (actionId, label, note, value) in new[]
            {
                ("seek-backward", "长按快退倍率", "按住「快退」快捷键时，每 200ms 连续回退「倍率 × 200ms」进度，不改变播放倍速", fixture.Settings.Playback.SeekBackwardHoldMultiplier),
                ("seek-forward", "长按快进倍速", "按住「快进」快捷键时，以该倍速连续快进", fixture.Settings.Playback.SeekForwardHoldMultiplier)
            })
            {
                var multiplier = allRows.FindIndex(row => row is SettingNumberRow number && number.Label == label);
                var action = allRows.FindIndex(row => row is SettingShortcutRow shortcut && shortcut.Id == actionId);
                Require(multiplier > 0 && action >= 0 && multiplier == action + 2
                    && allRows[multiplier] is SettingNumberRow { IsSubordinate: true } number
                    && number.Minimum == PlaybackSettings.MinimumSeekHoldMultiplier
                    && number.Maximum == PlaybackSettings.MaximumSeekHoldMultiplier
                    && number.Value == value
                    && number.Note == note,
                    $"{label} 没有挂在 {actionId} 的步长下面，或范围、初值、说明不对");
                Require(allRows[multiplier - 1] is SettingNumberRow { Label: "快进跨度（秒）" }
                    || allRows[multiplier - 1] is SettingNumberRow { Label: "快退跨度（秒）" },
                    $"{label} 没有紧跟在同向的「跨度（秒）」下面");
            }
            await SaveShortcutScreenAsync($"settings-shortcut-search-all-{width}-screen.png");

            await QueryAsync("字幕");
            Require(Shown().Any() && Shown().All(row => row.Label.Contains("字幕", StringComparison.Ordinal)), "操作名过滤不准确");
            Require(sources.All(source => ReferenceEquals(source.Rows, source.Section.Rows))
                && Shown().All(row => ReferenceEquals(row, originalRows[row.Id])), "搜索重建了行或 ItemsSource");
            Require(clear.Visibility == Visibility.Visible && page.ViewModel.ShortcutSearchSummary.StartsWith("找到 ", StringComparison.Ordinal), "查询回执或清空入口缺失");
            await SaveShortcutScreenAsync($"settings-shortcut-search-subtitle-{width}-screen.png");

            await QueryAsync("ctrl + f9");
            Require(Shown().Select(row => row.Id).SequenceEqual(new[] { "toggle-pause" }), "没有按当前自定义组合键过滤");
            var capture = Descendants(page).OfType<ShortcutCaptureBox>().Single(box => AutomationProperties.GetName(box) == "播放 / 暂停");
            // 点方框要由它自己接焦点：设置页的空白处处理器若把它当成空白，会立刻夺走焦点并退出抓键态。
            var captureLabel = Get<TextBlock>(capture, "ComboLabel");
            Require(SettingsPage.HandlesPointerFocus(captureLabel), "点快捷键抓键框仍会被设置页抢走焦点");
            Require(capture.Focus(FocusState.Programmatic), "快捷键抓键框不能取得焦点");
            page.OnSettingsPointerPressed(captureLabel);
            await LayoutAsync(page);
            Require(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), capture),
                "点快捷键抓键框时设置页把焦点交给了卡片滚动区");
            // 退出编辑（点空白处）应当落在滚动区本身：ScrollViewer 默认 IsTabStop=false，直接 Focus 会把焦点
            // 转给第一个子控件 —— 在快捷键卡上正好是搜索框，用户看到的就是「搜索框被自动触发」。
            var cards = Get<StackPanel>(page, "SettingsCardsContent");
            var scroll = Get<ScrollViewer>(page, "SettingsScroll");
            Require(!SettingsPage.HandlesPointerFocus(cards), "点空白处仍应交给卡片滚动区");
            Require(capture.Focus(FocusState.Programmatic), "快捷键抓键框不能取得焦点");
            page.OnSettingsPointerPressed(cards);
            await LayoutAsync(page);
            Require(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), scroll),
                "点空白处把焦点交给了快捷键搜索框");
            originalRows["toggle-pause"].OnCommitted(capture, "Ctrl+F8");
            await LayoutAsync(page);
            Require(!Shown().Any(), "改绑后旧键仍留在搜索结果");
            await QueryAsync("ctrl+f8");
            Require(Shown().Single().ComboText == "Ctrl + F8", "改绑后新键没有立即可搜索");
            await QueryAsync("空格");
            Require(!Shown().Any(), "已改绑的动作仍能按原默认键找到");

            await QueryAsync("Alt + [");
            Require(Shown().Single().Id == "subtitle-scale-down", "方括号组合键搜索失败");
            await QueryAsync("Del");
            Require(Shown().Single().Id == ShortcutCatalog.DismissSkip && AllRows().Count == 2,
                "Del 搜索应命中关闭跳过提示的可改行与恢复入口");
            await QueryAsync("回车");
            Require(Shown().Single().Id == ShortcutCatalog.TakeSkip && AllRows().Count == 2,
                "回车搜索应命中确认跳过的可改行与恢复入口");
            await QueryAsync("未设置");
            Require(Shown().Single().Id == "video-pan-left", "未设置搜索未显示解绑动作");
            await QueryAsync("Esc");
            Require(Shown().Single().Id == ShortcutCatalog.ExitPlayback && AllRows().Count == 2,
                "Esc 搜索应命中退出全屏／退出播放的可改行与恢复入口");
            originalRows[ShortcutCatalog.ExitPlayback].OnCommitted(capture, "Ctrl+F10");
            await LayoutAsync(page);
            Require(!Shown().Any(), "退出动作改绑后仍能用旧 Esc 找到");
            await QueryAsync("Ctrl+F10");
            Require(Shown().Single().Id == ShortcutCatalog.ExitPlayback, "退出动作没有按新键参与搜索");

            // 提示键与普通动作重叠时不弹替换框，也不交换普通动作的键；两条提示动作各自保持可达。
            originalRows[ShortcutCatalog.DismissSkip].OnCommitted(capture, "Ctrl+F8");
            originalRows[ShortcutCatalog.TakeSkip].OnCommitted(capture, "F");
            await LayoutAsync(page);
            Require(originalRows[ShortcutCatalog.DismissSkip].ComboText == "Ctrl + F8"
                && originalRows["toggle-pause"].ComboText == "Ctrl + F8"
                && originalRows[ShortcutCatalog.TakeSkip].ComboText == "F"
                && originalRows["toggle-fullscreen"].ComboText == "F",
                "跳过提示键与普通键重叠后没有即时刷新，或改变了普通动作");
            await QueryAsync("Ctrl+F8");
            Require(Shown().Select(row => row.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "toggle-pause", ShortcutCatalog.DismissSkip }),
                "共用按键搜索没有同时显示提示动作和普通动作");
            await QueryAsync("Del");
            Require(!Shown().Any(), "提示动作改绑后仍能用旧 Del 找到");
            await QueryAsync("回车");
            Require(!Shown().Any(), "提示动作改绑后仍能用旧回车找到");
            await QueryAsync("zzzz-not-a-shortcut");
            Require(!Shown().Any() && AllRows().Any(row => row.Label == "没有找到匹配的快捷键"), "空结果提示缺失");
            Require(AllRows().Any(row => row.Label == "恢复默认快捷键"), "搜索结果为空时丢了恢复默认入口");
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

            var reset = AllRows().OfType<SettingFactRow>().Single(row => row.Label == "恢复默认快捷键");
            reset.RunCommand.Execute(null);
            await LayoutAsync(page);
            Require(fixture.Settings.Shortcuts.Bindings.Count == 0, "搜索中恢复默认只清了可见动作");
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(page);
            Require(search.Text.Length == 0 && Shown().Count() == ShortcutCatalog.Actions.Count, "清空搜索没有恢复完整列表");
            Require(AllRows().Count == ShortcutCatalog.Actions.Count + 7
                && originalRows[ShortcutCatalog.ExitPlayback].ComboText == "Esc"
                && originalRows[ShortcutCatalog.DismissSkip].ComboText == "Del"
                && originalRows[ShortcutCatalog.TakeSkip].ComboText == "回车",
                "搜索中恢复默认后退出、关闭提示和确认跳过没有恢复默认键，或行数不正确");
            Require(sources.All(source => ReferenceEquals(source.Rows, source.Section.Rows)), "恢复默认替换了稳定集合");
            // 退出与跳过三条可改行在列表末尾，滚到底才看得到它们的实际换行与裁切；清空搜索后的
            // 新行表要等一拍布局，否则 ScrollableHeight 还是上一段短结果的高度。
            await LayoutAsync(page);
            await Task.Delay(200);
            scroll.UpdateLayout();
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Task.Delay(250);
            await SaveShortcutScreenAsync($"settings-shortcut-search-restored-{width}-screen.png");
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, $"settings-shortcut-search-{width}-evidence.txt"),
                $"逻辑：名称／当前键／改绑／解绑／空态／退出与提示键自定义／提示键重叠／能力隐藏恢复／分类保留／全部恢复／清空均通过。\n"
                + $"实屏空结果文字：显示 {shownEmptyPixels} 亮像素，隐藏 {hiddenEmptyPixels} 亮像素。\n");
            Require(shownEmptyPixels > 20, "空结果文字没有实际画到屏幕，不能把旧卡片截图当成快捷键页；控件逻辑已全部检查，视觉闸门仍未通过");
            Require(hiddenEmptyPixels == 0, "屏幕检查无法识别故意隐藏的空结果文字");

            int EmptyPixels()
            {
                var bounds = emptyText.TransformToVisual(windowRoot).TransformBounds(new Windows.Foundation.Rect(0, 0, emptyText.ActualWidth, emptyText.ActualHeight));
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
                Native.ShowWindow(handle, Native.SwShow);
                Native.SetForegroundWindow(handle);
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
            IEnumerable<SettingShortcutRow> Shown() => AllRows().OfType<SettingShortcutRow>();
            List<SettingRow> AllRows() => shortcutCards.SelectMany(section => section.Rows).ToList();
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

    private async Task ResizeSettingsProbeWindowAsync(Window window, int width, int height)
    {
        var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
        await WaitForDiagnosticsFrameAsync();
        Require(Native.GetClientRect(handle, out var client), "无法读取设置窗口客户区");
        File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "settings-window-resize.txt"),
            $"ResizeClient目标={width}x{height}；实际={client.Width}x{client.Height}；外框={window.AppWindow.Size}\n");
        if (client.Width != width || client.Height != height)
        {
            var outer = window.AppWindow.Size;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(
                outer.Width + width - client.Width, outer.Height + height - client.Height));
        }
        await UntilAsync(() => Native.GetClientRect(handle, out var actual)
            && Math.Abs(actual.Width - width) < 2 && Math.Abs(actual.Height - height) < 2);
    }

    private async Task PlayerAdditionalShortcutsAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "player-shortcuts"));
        paths.EnsureCreated();
        using var client = new DandanPlayClient(fixture.Settings, Path.Combine(paths.Root, "cache"), new RejectNetwork());
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
        var video = new MpvTrack(1, "video", null, "离线视频", true, true) { Codec = "h264", FfmpegIndex = 0 };
        var audio = new MpvTrack(2, "audio", "jpn", "离线音轨", true, true) { Codec = "aac", FfmpegIndex = 1 };
        var ass = new MpvTrack(3, "sub", "chi", "ASS 测试字幕", false, true) { Codec = "ass", MainSelection = 0, FfmpegIndex = 2 };
        var pgs = new MpvTrack(4, "sub", "chi", "PGS 测试字幕", false, true) { Codec = "hdmv_pgs_subtitle", Image = true, MainSelection = 0, FfmpegIndex = 3 };
        var second = new MpvTrack(5, "sub", "eng", "次字幕测试", false, true) { Codec = "subrip", MainSelection = 1, FfmpegIndex = 4 };
        var originalGeneration = Get<int>(vm, "_generation");
        try
        {
            SetTracks([video, audio, ass, pgs with { Selected = false }, second]);
            foreach (var property in PlayerMenuCatalog.Commands.SelectMany(node => node.RequireProperties)
                .Concat(PlayerMenuCatalog.CheckProperties).Distinct(StringComparer.Ordinal)) handle.Text[property] = "0";
            handle.Text["gpu-api"] = "d3d11";
            handle.Text["current-gpu-context"] = "d3d11";
            handle.Text["video-params/gamma"] = "pq";
            handle.Text["audio-device-list"] = "[{\"name\":\"auto\",\"description\":\"默认设备\"},{\"name\":\"wasapi/offline\",\"description\":\"离线耳机\"}]";
            handle.Text["audio-device"] = "auto";
            vm.BuildCurrentMenu(handle.Text);
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

            SetTracks([video, audio, ass with { Selected = false }, pgs, second]);
            vm.BuildCurrentMenu(handle.Text);
            // 字幕 → 兼容性那组快捷键（2026-11-02）：位图字幕的两条走内核的 stretch-image-subs-to-screen / sub-gray，
            // 重载是纯命令；这里对假后端核对实际发出的命令，不加载字幕也不播放。
            Require(vm.RunAdditionalShortcut("subtitle-pgs-margins"), "PGS 黑边没有处理器");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("cycle|stretch-image-subs-to-screen"), "PGS 黑边没有发送实际命令");
            Require(vm.RunAdditionalShortcut("subtitle-pgs-gray"), "PGS 灰度没有处理器");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("cycle|sub-gray"), "PGS 灰度没有发送实际命令");
            SetTracks([video, audio, ass, pgs with { Selected = false }, second]);
            vm.BuildCurrentMenu(handle.Text);
            Require(vm.RunAdditionalShortcut("subtitle-render-style"), "渲染样式没有处理器");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("cycle|sub-ass-override"), "渲染样式没有发送实际命令");
            Require(vm.RunAdditionalShortcut("subtitle-reload"), "重载字幕没有处理器");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Contains("sub-reload"), "重载字幕没有发送实际命令");
            Require(vm.RunAdditionalShortcut("speed-double") && vm.SpeedValue == 2, "倍速快捷键没有同步 UI 状态");
            vm.SetDelay(subtitle: true, 1.2);
            Require(vm.RunAdditionalShortcut("subtitle-delay-reset") && vm.SubtitleDelay == 0 && handle.Numbers["sub-delay"] == 0, "字幕归零未同步状态和命令");
            Require(vm.RunAdditionalShortcut("audio-delay-reset"), "音频归零没有处理器");
            await UntilAsync(() => handle.Commands.Contains("set|audio-delay|0"));

            await CheckDynamicMenusAsync();
            await CheckNativeShortcutQueueAsync();

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
            var menu = Get<MenuFlyout>(page, "PictureMenu");
            menu.Hide();
            _root.Children.Remove(page);
            page.Detach();
            page.Shutdown();
            Field(playback, "_current", null);
            Field(playback, "_active", null);
            await vm.ShutdownAsync();
        }

        void SetTracks(IReadOnlyList<MpvTrack> value)
        {
            Field(vm, "<Tracks>k__BackingField", value);
            handle.Tracks = value;
        }

        async Task CheckNativeShortcutQueueAsync()
        {
            var bindings = fixture.Settings.Shortcuts.Bindings;
            var savedBindings = bindings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var savedPictureInHostWindow = handle.PictureInHostWindow;
            var savedInteractionVersion = Get<int>(vm, "_interactionVersion");
            var savedCache = Get<string?>(vm, "_nativeShortcutsSent");
            TaskCompletionSource<bool>? gate = null;
            var evidence = new List<string>();
            try
            {
                handle.PictureInHostWindow = false;
                Require(vm.HeadlessPlayback && playback.IsPlaying, "原生快捷键队列没有进入假独占在播环境");
                bindings.Clear();
                Field(vm, "_nativeShortcutsSent", null);
                await PushAsync();
                var jsonA = handle.NativeShortcutPayloads.Last();
                Require(handle.NativeShortcutsApplied == jsonA && Get<string?>(vm, "_nativeShortcutsSent") == jsonA,
                    "原生快捷键初始映射没有实际送达并缓存");

                // A 已缓存时让 B 卡在真实 VM -> PlaybackService -> 假后端链上，再请求 A。
                // 在途刷新菜单不应使同一场播放的映射失效，也不能因旧缓存 A 而漏发最后的 A。
                var before = handle.NativeShortcutPayloads.Count;
                bindings["toggle-pause"] = "Ctrl+F9";
                gate = Pending<bool>();
                handle.NativeShortcutPending = gate;
                var blockedB = PushAsync();
                await UntilAsync(() => handle.NativeShortcutPayloads.Count == before + 1);
                var jsonB = handle.NativeShortcutPayloads.Last();
                Require(jsonA != jsonB && !blockedB.IsCompleted, "原生快捷键 B 映射没有被假后端阻塞");
                Field(vm, "_interactionVersion", savedInteractionVersion + 1);
                bindings.Clear();
                var restoreA = PushAsync();
                Require(handle.NativeShortcutPayloads.Count == before + 1 && !restoreA.IsCompleted,
                    "原生快捷键 A 越过了在途 B，或没有等待发送队列");
                gate.SetResult(true);
                await Task.WhenAll(blockedB, restoreA);
                gate = null;
                Require(handle.NativeShortcutPayloads.Skip(before).SequenceEqual([jsonB, jsonA])
                    && handle.NativeShortcutsApplied == jsonA && Get<string?>(vm, "_nativeShortcutsSent") == jsonA
                    && handle.NativeShortcutMaxConcurrent == 1, "原生快捷键 A-B-A 没有串行恢复到最后请求，或菜单刷新误伤同场播放");
                evidence.Add("cached A -> blocked B -> requested A: applied A; max concurrent sends=1; menu refresh preserved playback identity");

                // 同映射的 force 请求有独立责任，后续普通重复请求不能把排队中的 force 合并掉。
                before = handle.NativeShortcutPayloads.Count;
                gate = Pending<bool>();
                handle.NativeShortcutPending = gate;
                var blockedForce = PushAsync(force: true);
                await UntilAsync(() => handle.NativeShortcutPayloads.Count == before + 1);
                var queuedForce = PushAsync(force: true);
                var ordinaryDuplicate = PushAsync();
                Require(!blockedForce.IsCompleted && !queuedForce.IsCompleted && !ordinaryDuplicate.IsCompleted
                    && handle.NativeShortcutPayloads.Count == before + 1, "同映射 force 请求没有留在串行队列");
                gate.SetResult(true);
                await Task.WhenAll(blockedForce, queuedForce, ordinaryDuplicate);
                gate = null;
                Require(handle.NativeShortcutPayloads.Skip(before).SequenceEqual([jsonA, jsonA])
                    && handle.NativeShortcutsApplied == jsonA && handle.NativeShortcutMaxConcurrent == 1,
                    "排队中的 force 原生快捷键推送被普通重复请求吞掉，或重复请求未去重");
                evidence.Add("same JSON: blocked force + queued force + ordinary duplicate produced exactly two serial sends");

                // 先直接核对生产 PlaybackService 将后端异常转换为 false，再核对 VM 队列自身可重试。
                handle.NativeShortcutThrowNext = true;
                Require(!await playback.CommandAsync("script-message", VideoWindowContract.Shortcuts, jsonB),
                    "原生快捷键后端异常没有被 PlaybackService 转换为失败回执");
                Require(handle.NativeShortcutsApplied == jsonA, "失败的原生快捷键推送仍覆盖了已应用映射");
                bindings["toggle-pause"] = "Ctrl+F9";
                handle.NativeShortcutThrowNext = true;
                before = handle.NativeShortcutPayloads.Count;
                await PushAsync();
                Require(handle.NativeShortcutPayloads.Count == before + 1 && handle.NativeShortcutsApplied == jsonA
                    && Get<string?>(vm, "_nativeShortcutsSent") == jsonA, "失败的 VM 推送错误地更新了原生快捷键缓存");
                await PushAsync();
                Require(handle.NativeShortcutPayloads.Skip(before).SequenceEqual([jsonB, jsonB])
                    && handle.NativeShortcutsApplied == jsonB && Get<string?>(vm, "_nativeShortcutsSent") == jsonB
                    && handle.NativeShortcutMaxConcurrent == 1, "原生快捷键推送失败后没有以同一映射成功恢复");
                evidence.Add("backend throw: PlaybackService returned false; failed VM send retained A; next same-B request succeeded and cached B");
                File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "native-shortcut-queue-evidence.txt"), evidence);
            }
            finally
            {
                gate?.TrySetResult(true);
                handle.NativeShortcutPending?.TrySetResult(true);
                handle.NativeShortcutPending = null;
                await Get<Task>(vm, "_nativeShortcutTail");
                bindings.Clear();
                foreach (var pair in savedBindings) bindings[pair.Key] = pair.Value;
                handle.PictureInHostWindow = savedPictureInHostWindow;
                Field(vm, "_interactionVersion", savedInteractionVersion);
                Field(vm, "_nativeShortcutsSent", savedCache);
            }

            Task PushAsync(bool force = false) => (Task)Call(vm, "PushNativeShortcutsAsync", force)!;
        }

        async Task CheckDynamicMenusAsync()
        {
            var readings = new List<string>();
            var menu = Get<MenuFlyout>(page, "PictureMenu");
            Require(Rows().Any(row => row.Commands.Any(command => command.Count > 2 && command[1] == "audio-device")), "音频设备列表缺失");
            foreach (var property in new[] { "vid", "aid", "sid", "secondary-sid" })
                Require(Rows().Any(row => row.Commands.Any(command => command.Count > 2 && command[1] == property)), "动态轨道缺失:" + property);
            var assMenu = Rebuild();
            Require(assMenu.Any(row => row.Shortcut == "subtitle-border-scale") && !assMenu.Any(row => row.Shortcut == "subtitle-pgs-gray"), "ASS/PGS操作显隐错误");
            var firstCrop = MenuRows(menu.Items).Single(row => row.Tag is PlayerMenuNode { Shortcut: "video-crop" });
            fixture.Settings.Shortcuts.Bindings["video-crop"] = "Ctrl+F10";
            Rebuild();
            var secondCrop = MenuRows(menu.Items).Single(row => row.Tag is PlayerMenuNode { Shortcut: "video-crop" });
            Require(!ReferenceEquals(firstCrop, secondCrop) && secondCrop.KeyboardAcceleratorTextOverride == "Ctrl + F10", "菜单未重建或快捷键提示未更新");
            fixture.Settings.Shortcuts.Bindings.Remove("video-crop");
            SetTracks([video, audio, ass with { Selected = false }, pgs with { Selected = false }, second]);
            var off = Rebuild();
            Require(off.Any(row => row.Shortcut == "subtitle-reset") && off.Any(row => row.Shortcut == "subtitle-next"), "关闭字幕丢失恢复入口");
            Require(!off.Any(row => row.Shortcut is "subtitle-border-scale" or "subtitle-pgs-gray"), "关闭主字幕仍显示格式操作");
            SetTracks([video, audio, ass with { Selected = false }, pgs, second with { Selected = false }]);
            var image = Rebuild();
            Require(image.Any(row => row.Shortcut == "subtitle-pgs-gray") && !image.Any(row => row.Shortcut == "subtitle-border-scale"), "PGS操作未恢复");
            Require(image.Any(row => row.Shortcut == "secondary-subtitle-next") && !image.Any(row => row.Shortcut == "secondary-subtitle-position-up"), "次字幕关闭仍显示位移操作或丢失选择入口");
            SetTracks([video, audio, ass, pgs with { Selected = false }, second]);
            Require(Rebuild().Any(row => row.Shortcut == "secondary-subtitle-position-up"), "次字幕恢复未恢复位移操作");
            var old = handle.Commands.Count;
            handle.Text["sigmoid-upscaling"] = null;
            Require(!Rebuild().Any(row => row.Shortcut == "video-nonlinear"), "内核属性未知仍显示无效操作");
            vm.RunAdditionalShortcut("video-nonlinear");
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Skip(old).All(command => !command.Contains("sigmoid-upscaling", StringComparison.Ordinal)), "隐藏动作仍向内核发送无效属性");
            handle.Text["sigmoid-upscaling"] = "yes";
            SetTracks([video, audio, ass, pgs with { Selected = false }, second]);
            page.Visibility = Visibility.Visible;
            Rebuild();
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 820);
                await LayoutAsync(page);
                Native.ShowWindow(_window!.Handle, Native.SwShow);
                Native.SetForegroundWindow(_window.Handle);
                _window.TopMost = true;
                menu.ShowAt(page, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                { Position = new Windows.Foundation.Point(80, 80) });
                await LayoutAsync(page);
                Require(menu.IsOpen, "播放右键菜单未打开");
                Require(CompactRows(menu.Items),
                    "右键菜单或子菜单没有使用紧凑字号");
                var navigation = menu.Items.OfType<MenuFlyoutSubItem>().Single(group => group.Text == UiText.Translate("导航"));
                var navigationPeer = FrameworkElementAutomationPeer.CreatePeerForElement(navigation)?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider
                    ?? throw new InvalidOperationException("导航子菜单没有可展开入口");
                navigation.Focus(FocusState.Programmatic);
                navigationPeer.Expand();
                var backgroundClick = navigation.Items.OfType<ToggleMenuFlyoutItem>().Single(row =>
                    row.Tag is PlayerMenuNode { Shortcut: PlayerMenuCatalog.BackgroundClickShortcut });
                await UntilAsync(() => backgroundClick.IsLoaded);
                var hint = Descendants(backgroundClick).OfType<TextBlock>()
                    .SingleOrDefault(part => part.Name == "KeyboardAcceleratorTextBlock");
                Require(hint is { Text: "B", TextAlignment: TextAlignment.Right, Visibility: Visibility.Visible },
                    "导航里的后台单击开关没有在右侧显示 B");
                navigationPeer.Collapse();
                var subtitleGroup = menu.Items.OfType<MenuFlyoutSubItem>().Single(group => group.Text == "字幕");
                var expansion = FrameworkElementAutomationPeer.CreatePeerForElement(subtitleGroup)?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider
                    ?? throw new InvalidOperationException("字幕子菜单没有可展开入口");
                subtitleGroup.Focus(FocusState.Programmatic);
                expansion.Expand();
                await UntilAsync(() => expansion.ExpandCollapseState == ExpandCollapseState.Expanded);
                page.UpdateLayout();
                await WaitForDiagnosticsFrameAsync();
                await Task.Delay(250);
                Require(expansion.ExpandCollapseState == ExpandCollapseState.Expanded, "字幕子菜单没有实际展开");
                var childRow = subtitleGroup.Items.OfType<MenuFlyoutItem>().First();
                var bounds = FrameworkElementAutomationPeer.CreatePeerForElement(childRow)!.GetBoundingRectangle();
                Require(bounds.Width > 0 && bounds.Height > 0, "字幕子菜单没有真实屏幕坐标");
                // The island's in-process peer reports root-relative logical coordinates.
                var origin = new Momoka.Shell.Interop.NativePoint();
                Require(Native.ClientToScreen(_window.Handle, ref origin), "菜单宿主屏幕原点不可用");
                var scale = page.XamlRoot.RasterizationScale;
                var point = new Momoka.Shell.Interop.NativePoint
                { X = origin.X + (int)((bounds.X + bounds.Width / 2) * scale), Y = origin.Y + (int)((bounds.Y + bounds.Height / 2) * scale) };
                var popupHandle = Native.GetAncestor(Native.WindowFromPoint(point), Native.GaRoot);
                Native.GetWindowThreadProcessId(popupHandle, out var owner);
                File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "player-menu-pixels.txt"),
                    $"width={width}; bounds={bounds}; point={point.X},{point.Y}; hwnd={popupHandle:X}; owner={owner}\n");
                Require(owner == (uint)Environment.ProcessId, "菜单像素不属于本次探针");
                await CompositionPlaybackProbe.SaveWindowScreenAsync(popupHandle,
                    Path.Combine(_options.Paths.LogDirectory, $"player-menu-expanded-{width}-screen.png"));
                readings.Add($"width={width}; rows={Rows().Count}; uiRows={MenuRows(menu.Items).Count()}; hints=current; ASS/PGS/secondary/tool restoration verified");
                menu.Hide();
            }
            // Retain a real row after rebuilding, then invalidate the original playback identity before Invoke.
            var stale = MenuRows(menu.Items).Single(row => row.Tag is PlayerMenuNode { Shortcut: "video-crop" });
            var peer = new MenuFlyoutItemAutomationPeer(stale);
            var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider
                ?? throw new InvalidOperationException("菜单操作没有自动化Invoke入口");
            var before = handle.Commands.Count;
            Field(vm, "_generation", originalGeneration + 1);
            invoke.Invoke();
            await LayoutAsync(page);
            await Get<Task>(vm, "_pictureCommandTask");
            Require(handle.Commands.Count == before, "旧菜单行仍执行到了替换播放");
            Field(vm, "_generation", originalGeneration);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "player-menu-expanded-evidence.txt"), readings);

            List<PlayerMenuNode> Rows() => PlayerMenuCatalog.Flatten(vm.BuildCurrentMenu(handle.Text))
                .Where(node => node.Kind == PlayerMenuKind.Command).ToList();
            List<PlayerMenuNode> Rebuild()
            {
                vm.BuildCurrentMenu(handle.Text);
                Call(page, "OnPictureMenuOpening", page, new object());
                return Rows();
            }
            static bool CompactRows(IEnumerable<MenuFlyoutItemBase> items) => items.All(item =>
                Math.Abs(item.FontSize - 12.75) < 0.01 && (item is not MenuFlyoutSubItem group || CompactRows(group.Items)));

            static IEnumerable<MenuFlyoutItem> MenuRows(IEnumerable<MenuFlyoutItemBase> items)
            {
                foreach (var item in items)
                {
                    if (item is MenuFlyoutItem row) yield return row;
                    if (item is MenuFlyoutSubItem group)
                        foreach (var child in MenuRows(group.Items)) yield return child;
                }
            }
        }
    }

    private sealed class ShortcutProbeHandle : IPlaybackHandle, IPlayerControl
    {
        public List<string> Commands { get; } = [];
        public Dictionary<string, double> Numbers { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string?> Text { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<MpvTrack> Tracks { get; set; } = [];
        public Func<IReadOnlyList<string>, bool> Accept { get; set; } = _ => true;
        public TaskCompletionSource<bool>? Pending { get; set; }
        public TaskCompletionSource<bool>? NativeShortcutPending { get; set; }
        public bool NativeShortcutThrowNext { get; set; }
        public List<string> NativeShortcutPayloads { get; } = [];
        public string? NativeShortcutsApplied { get; private set; }
        public int NativeShortcutMaxConcurrent { get; private set; }
        private int _nativeShortcutInFlight;
        public bool HasControlChannel => true;
        public bool? PictureInHostWindow { get; set; } = true;
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
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult(Tracks);
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(Numbers.GetValueOrDefault(name));
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Text.GetValueOrDefault(name));
        public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(string.Join('|', arguments));
            if (arguments.Count > 2 && arguments[0] == "script-message" && arguments[1] == VideoWindowContract.Shortcuts)
                return SendNativeShortcutsAsync(arguments);
            if (arguments.Count > 2 && arguments[0] == "set" && double.TryParse(arguments[2], CultureInfo.InvariantCulture, out var number)) Numbers[arguments[1]] = number;
            if (Pending is not { } pending) return Task.FromResult(Accept(arguments));
            Pending = null;
            return pending.Task;
        }
        private async Task<bool> SendNativeShortcutsAsync(IReadOnlyList<string> arguments)
        {
            var json = arguments[2];
            NativeShortcutPayloads.Add(json);
            var concurrent = Interlocked.Increment(ref _nativeShortcutInFlight);
            NativeShortcutMaxConcurrent = Math.Max(NativeShortcutMaxConcurrent, concurrent);
            var pending = NativeShortcutPending;
            NativeShortcutPending = null;
            try
            {
                if (NativeShortcutThrowNext)
                {
                    NativeShortcutThrowNext = false;
                    throw new InvalidOperationException("离线原生快捷键队列预期失败");
                }
                var accepted = pending is null ? Accept(arguments) : await pending.Task.ConfigureAwait(false);
                if (accepted) NativeShortcutsApplied = json;
                return accepted;
            }
            finally { Interlocked.Decrement(ref _nativeShortcutInFlight); }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
