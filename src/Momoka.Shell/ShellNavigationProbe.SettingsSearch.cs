using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
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
        _root.Children.Add(page);
        ThemeHost.Register(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            var categories = page.ViewModel.Categories.ToArray();
            var search = Get<AutoSuggestBox>(page, "SettingsSearchBox");
            var scroll = Get<ScrollViewer>(page, "SettingsScroll");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                page.SelectedCategory = "主页";
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                var navigation = Get<ListView>(page, "CategoryNavigation");
                Require(navigation.CornerRadius == new CornerRadius(0), "分类列表仍有卡片圆角");
                Require(navigation.Background is SolidColorBrush { Color.A: 0 }, "分类列表仍有卡片底色");
                Require(Get<TextBlock>(page, "SettingsPageTitle") is { Text: "设置", Visibility: Visibility.Visible }, "设置页面标题被误删");
                Require(Descendants(page).OfType<ShelfHead>().Count() == 0, "设置正文仍有重复分类与数量徽标");
                var homeTitle = Descendants(page).OfType<TextBlock>().Single(title => title.Name == "SettingsSectionTitle" && title.Text == "主页");
                Require(homeTitle.Visibility == Visibility.Collapsed, "主页重复分类标题未移除");
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
                var submit = Descendants(search).OfType<Button>().Single(button => button.Name == "QueryButton");
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
                await SaveProbeScreenAsync($"settings-search-empty-{width}-screen.png");
                await QueryAsync("");
                Require(Get<TextBlock>(page, "SettingsSearchEmpty").Visibility == Visibility.Collapsed, "清空后仍残留搜索空态");
                page.SelectedCategory = "字幕";
                scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                await LayoutAsync(page);
                try { await UntilAsync(() => scroll.VerticalOffset < 1); }
                catch (TimeoutException) { throw new InvalidOperationException($"分类复位未完成：宽{width}，offset{scroll.VerticalOffset:0.0}，extent{scroll.ExtentHeight:0.0}"); }
                var bar = Descendants(scroll).OfType<ScrollBar>().Single(bar => bar.Name == "VerticalScrollBar" && IsOwnScrollBar(scroll, bar));
                var barBox = Box(scroll, bar);
                var cards = Descendants(page).OfType<FrameworkElement>().Where(element => element.Name == "SettingsSection" && element.Visibility == Visibility.Visible);
                Require(Descendants(page).OfType<TextBlock>().Any(title => title.Name == "SettingsSectionTitle"
                    && title.Visibility == Visibility.Visible && title.Text == "实时字幕翻译"), "字幕翻译分组标题被错误删除");
                Require(cards.Any() && barBox.Width > 0 && cards.All(card => Box(scroll, card).Right <= barBox.X - 3),
                    $"滚动条覆盖设置卡片：指示器 x={barBox.X:0.0} 宽={barBox.Width:0.0}，卡片右边="
                    + string.Join(",", cards.Select(card => Box(scroll, card).Right.ToString("0.0"))));
                foreach (var current in width == 1100 ? UiThemes.All : [UiThemes.Default])
                {
                    ThemeHost.Apply(current.Id);
                    await LayoutAsync(page);
                    await SaveReturnFrameAsync(page, $"settings-clean-{current.Id}-{width}-render.png");
                    await WaitForDiagnosticsFrameAsync();
                    await SaveProbeScreenAsync($"settings-clean-{current.Id}-{width}-screen.png");
                }
                scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
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
