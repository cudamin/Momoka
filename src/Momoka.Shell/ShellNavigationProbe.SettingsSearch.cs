using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Interop;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>实际控件的搜索、导航、标题移除、无分隔线、滚动条避让；全程隔离假服务和偏好。</summary>
    private async Task SettingsSearchLayoutAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Mpv.ScreenshotFormat = "webp";
        fixture.Settings.Mpv.ScreenshotWebpLossless = false;
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-search-layout"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        var theme = ThemeHost.Current.Id;
        var preference = HomeMotion.PreferenceEnabled;
        HomeMotion.ApplyPreference(false);
        var settingsWindow = SettingsWindow.CreateCaptionedWindow(page);
        _shortcutProbeWindows.Add(settingsWindow);
        var windowRoot = (Grid)settingsWindow.Content;
        var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(settingsWindow.AppWindow.Id);
        var ownerWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(ownerWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        settingsWindow.AppWindow.Move(new Windows.Graphics.PointInt32(workArea.X + 24, workArea.Y + 24));
        settingsWindow.Activate();
        var presenter = settingsWindow.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter
            ?? throw new InvalidOperationException("设置探针窗口不是普通窗口");
        var wasTopMost = presenter.IsAlwaysOnTop;
        presenter.IsAlwaysOnTop = true;
        ThemeHost.Register(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            var categories = page.ViewModel.Categories.ToArray();
            var search = Get<AutoSuggestBox>(page, "SettingsSearchBox");
            var scroll = Get<ScrollViewer>(page, "SettingsScroll");
            foreach (var width in new[] { 1100, 1440, 760 })
            {
                await ResizeSettingsProbeWindowAsync(settingsWindow, width, 900);
                page.SelectedCategory = "主页";
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                var navigation = Get<ListView>(page, "CategoryNavigation");
                var caption = windowRoot.Children.OfType<Border>().Single(child => child.Name == "SettingsWindowTitleBar");
                Require(caption.ActualHeight >= 32 && Box(windowRoot, search).Top >= Box(windowRoot, caption).Bottom,
                    "搜索框没有排在独立标题栏下方");
                Require(!presenter.IsMinimizable && !presenter.IsMaximizable, "设置窗口仍有最小化或最大化按钮");
                if (width >= 1100) Require(Math.Abs(search.ActualWidth - 200) < 1, "放大窗口后侧栏搜索宽度发生变化");
                Require(navigation.CornerRadius == new CornerRadius(0), "分类列表仍有卡片圆角");
                Require(navigation.Background is SolidColorBrush { Color.A: 0 }, "分类列表仍有卡片底色");
                var searchInput = Descendants(search).OfType<TextBox>().Single(input => input.Name == "TextBox");
                Require(searchInput.Focus(FocusState.Programmatic), "搜索框无法取得焦点");
                await LayoutAsync(page);
                var searchBorder = Descendants(searchInput).OfType<Border>().Single(border => border.Name == "BorderElement");
                Require(searchBorder.BorderThickness == new Thickness(1), "搜索框聚焦边框四边不等粗");
                var content = Get<Grid>(page, "SettingsContent");
                Require(!content.Children.OfType<TextBlock>().Any(title => title.Text == "设置"), "正文顶部仍有设置标题");
                Require(Math.Abs(Box(content, scroll).Top) < 1 && Math.Abs(scroll.ActualHeight - content.ActualHeight) < 1,
                    "标题移除后正文仍留有空白带");
                Require(Descendants(page).OfType<ShelfHead>().Count() == 0, "设置正文仍有重复分类与数量徽标");
                // 主页 2026-10-27 拆成两张卡（轮播图 / 主页版面）：每张卡都该显示自己的标题。
                // 按 DataContext 认「这张卡此刻是可见的那一组」—— 没选中的卡也建着，标题的 Visibility 仍是 Visible。
                var cardTitles = Descendants(page).OfType<TextBlock>()
                    .Where(title => title.Name == "SettingsSectionTitle" && title.DataContext is SettingSection { IsVisible: true })
                    .Select(title => title.Text).ToArray();
                Require(cardTitles.SequenceEqual(["轮播图", "主页版面"]),
                    "主页分类的卡片标题不对：" + string.Join('、', cardTitles));
                foreach (var list in Descendants(page).OfType<ItemsControl>().Where(list => list.Name == "SettingsRowsList"
                    && list.Tag is SettingSection { IsVisible: true }))
                {
                    Require(VisualTreeHelper.GetParent(list) is Border { BorderThickness: var border } && border == new Thickness(0),
                        "设置卡片仍有分隔边框");
                    var panel = list.ItemsPanelRoot as StackPanel;
                    Require(panel is not null && panel.Spacing == 0,
                        $"可见设置行仍用间隙画分隔线：{(list.Tag as SettingSection)?.Title}，面板={list.ItemsPanelRoot?.GetType().Name ?? "未实现"}");
                }
                await QueryAsync("WebP 质量");
                Require(page.ViewModel.SettingsSearchResults.Any(result => result.Label == "WebP 质量"), "未搜索到具体设置行");
                var submit = Get<Button>(page, "SettingsSearchButton");
                Require(Box(search, submit).Left >= 0 && Box(search, submit).Right < search.ActualWidth / 2,
                    "搜索图标没有位于输入框左侧");
                ((IInvokeProvider)new ButtonAutomationPeer(submit).GetPattern(PatternInterface.Invoke)).Invoke();
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                Require(page.SelectedCategory == "播放行为", "提交搜索没有导航到目标分类");
                var result = page.ViewModel.SettingsSearchResults.First(result => result.Label == "WebP 质量");
                var rows = Descendants(page).OfType<ItemsControl>().Single(list => ReferenceEquals(list.ItemsSource, result.Section!.Rows));
                Require(rows.ContainerFromItem(result.Row) is FrameworkElement, "搜索目标行没有实际实现");
                var target = (FrameworkElement)rows.ContainerFromItem(result.Row);
                try { await UntilAsync(() => Box(scroll, target).Bottom > 0 && Box(scroll, target).Top < scroll.ActualHeight); }
                catch (TimeoutException)
                {
                    var missed = Box(scroll, target);
                    throw new InvalidOperationException($"搜索定位未完成：宽{width}，分类{page.SelectedCategory}，目标{missed.Top:0.0}..{missed.Bottom:0.0}，视口{scroll.ActualHeight:0.0}，offset{scroll.VerticalOffset:0.0}，extent{scroll.ExtentHeight:0.0}；{page.SearchNavigationReading}");
                }
                var rowBox = Box(scroll, target);
                Require(rowBox.Bottom > 0 && rowBox.Top < scroll.ActualHeight, "搜索后目标仍在可视区外");
                Require(page.ViewModel.Categories.SequenceEqual(categories), "搜索过滤或重建了侧栏分区");
                await QueryAsync("不存在的设置项9df7");
                Require(Get<TextBlock>(page, "SettingsSearchEmpty").Visibility == Visibility.Visible, "无结果提示未显示");
                await SaveReturnFrameAsync(page, $"settings-search-empty-{width}-render.png");
                await WaitForDiagnosticsFrameAsync();
                await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory, $"settings-search-empty-{width}-screen.png"));
                await QueryAsync("");
                Require(Get<TextBlock>(page, "SettingsSearchEmpty").Visibility == Visibility.Collapsed, "清空后仍残留搜索空态");
                page.SelectedCategory = "字幕";
                scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                await LayoutAsync(page);
                try { await UntilAsync(() => scroll.VerticalOffset < 1); }
                catch (TimeoutException) { throw new InvalidOperationException($"分类复位未完成：宽{width}，offset{scroll.VerticalOffset:0.0}，extent{scroll.ExtentHeight:0.0}"); }
                var bar = Descendants(scroll).OfType<ScrollBar>().Single(bar => bar.Name == "VerticalScrollBar" && IsOwnScrollBar(scroll, bar));
                var barBox = Box(scroll, bar);
                Require(Math.Abs(Box(windowRoot, bar).Right - windowRoot.ActualWidth) < 1,
                    "设置滚动条未贴窗口右侧");
                var cards = Descendants(page).OfType<FrameworkElement>().Where(element => element.Name == "SettingsSection" && element.Visibility == Visibility.Visible);
                Require(Descendants(page).OfType<TextBlock>().Any(title => title.Name == "SettingsSectionTitle"
                    && title.Visibility == Visibility.Visible && title.Text == "实时字幕翻译"), "字幕翻译分组标题被错误删除");
                Require(cards.Any() && barBox.Width > 0 && cards.All(card => Box(scroll, card).Right <= barBox.X - 3),
                    $"滚动条覆盖设置卡片：指示器 x={barBox.X:0.0} 宽={barBox.Width:0.0}，卡片右边="
                    + string.Join(",", cards.Select(card => Box(scroll, card).Right.ToString("0.0"))));
                var cardsContent = Get<StackPanel>(page, "SettingsCardsContent");
                Require(cards.All(card => Math.Abs(card.ActualWidth - cardsContent.ActualWidth) < 1),
                    "设置卡片没有随可用正文宽度展开");
                foreach (var current in width == 1100 ? UiThemes.All : [UiThemes.Default])
                {
                    ThemeHost.Apply(current.Id);
                    windowRoot.Background = new SolidColorBrush(ThemeHost.ToColor(current.Colors.Window));
                    await LayoutAsync(page);
                    await SaveReturnFrameAsync(page, $"settings-clean-{current.Id}-{width}-render.png");
                    await WaitForDiagnosticsFrameAsync();
                    await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory, $"settings-clean-{current.Id}-{width}-screen.png"));
                }
                scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }
        finally
        {
            page.Release();
            if (page.XamlRoot is { } root)
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root)) popup.IsOpen = false;
            presenter.IsAlwaysOnTop = wasTopMost;
            settingsWindow.AppWindow.Hide();
            settingsWindow.Content = null;
            ThemeHost.Apply(theme);
            HomeMotion.ApplyPreference(preference);
            ResizeInspect(1420, 980);
        }

        async Task QueryAsync(string text)
        {
            // 真实 AutoSuggestBox 的程序化填词事件，与键入共用正式 handler；不直接改 VM 冒充输入。
            var input = Get<AutoSuggestBox>(page, "SettingsSearchBox");
            input.Text = text;
            await LayoutAsync(page);
            Require(page.ViewModel.SettingsSearchQuery == text,
                $"设置搜索事件未接通：输入框={input.Text}，搜索状态={page.ViewModel.SettingsSearchQuery}");
        }
        static bool IsOwnScrollBar(ScrollViewer owner, DependencyObject bar)
        {
            for (var parent = VisualTreeHelper.GetParent(bar); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer viewer) return ReferenceEquals(viewer, owner);
            return false;
        }
        static Rect Box(UIElement anchor, FrameworkElement element) => element.TransformToVisual(anchor)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }
}
