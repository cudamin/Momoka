using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Interop;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SidebarAsync()
    {
        var originalAnimations = HomeMotion.PreferenceEnabled;
        var originalTheme = ThemeHost.Current;
        using var fixture = CreateFixture();
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.ShowHomeBanner = true;
        var picture = await FixtureImageAsync();
        var favorite = false;
        var movies = Enumerable.Range(0, 20).Select(index => new EmbyItem
        {
            Id = "sidebar-movie-" + index,
            Name = "离线影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            if (path.EndsWith("/Views", StringComparison.Ordinal))
            {
                var prefix = request.RequestUri.Host == "b.invalid" ? "B" : "A";
                return Task.FromResult(Json(new
                {
                    Items = new[]
                    {
                        new EmbyItem { Id = prefix + "-movies", Name = prefix + " 的电影", Type = EmbyItemType.CollectionFolder, CollectionType = "movies" },
                        new EmbyItem { Id = prefix + "-shows", Name = prefix + " 的电视剧", Type = EmbyItemType.CollectionFolder, CollectionType = "tvshows" },
                        new EmbyItem { Id = "music", Name = "不应出现的音乐库", Type = EmbyItemType.CollectionFolder, CollectionType = "music" }
                    }
                }));
            }
            if (Query(request, "Filters")?.Contains("IsFavorite", StringComparison.Ordinal) == true)
                return Task.FromResult(Json(new { Items = favorite ? movies.Take(1).ToArray() : [], TotalRecordCount = favorite ? 1 : 0 }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(movies.Take(6).ToArray()));
            if (path.EndsWith("/Items", StringComparison.Ordinal) || path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = movies, TotalRecordCount = movies.Length }));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        // Only lend the already-running probe window for title-bar geometry; no player is attached.
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var page = new SettingsPage();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "sidebar-settings"));
        paths.EnsureCreated();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        page.SelectedCategory = "界面";
        page.Visibility = Visibility.Collapsed;
        _root.Children.Add(page);

        var nav = Get<NavigationView>(shell, "Sidebar");
        var host = Get<Grid>(shell, "SidebarHost");
        var frame = shell.Pages;
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1240, 900);
            await LayoutAsync(shell);
            Require(!fixture.Settings.Ui.SidebarMode && host.Visibility == Visibility.Collapsed, "默认侧栏没有关闭");
            await ToggleAsync(true);
            Require(host.Visibility == Visibility.Collapsed && Get<Button>(shell, "SettingsButton").Visibility == Visibility.Visible,
                "未登录时侧栏出现或设置入口丢失");
            await ToggleAsync(false);
            await fixture.SelectAsync();
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await LayoutAsync(shell);
            await Task.Delay(250);
            await SaveProbeScreenAsync("sidebar-off-1240-screen.png");
            var home = (HomePage)frame.Content;
            var shelves = home.ViewModel.Shelves.ToArray();
            int DataRequests() => fixture.Transport.Requests.Count(request => !request.Contains("/Images/", StringComparison.Ordinal));
            var requests = DataRequests();
            await ToggleAsync(true);
            await LayoutAsync(shell);
            Require(host.Visibility == Visibility.Visible && nav.IsPaneOpen && Math.Abs(host.ActualWidth - 220) < 1,
                "宽窗侧栏未展开");
            Require(ReferenceEquals(home, frame.Content) && shelves.SequenceEqual(home.ViewModel.Shelves)
                && DataRequests() == requests, "切换侧栏重建主页货架或重新请求了数据");
            Require(shell.LibraryViews.Count == 2 && nav.MenuItems.OfType<NavigationViewItem>().Count(item =>
                item.Tag is string tag && tag.StartsWith("library:", StringComparison.Ordinal)) == 2, "媒体库数量或音乐过滤错误");
            Require(home.BleedRead().Ok, "侧栏模式轮播没有铺满内容区：" + home.BleedRead().Detail);
            AssertSidebarGeometry(shell, host, true);
            DumpSidebarLayout(shell, "1240 expanded");
            await SaveProbeScreenAsync("sidebar-on-1240-screen.png");

            Select(nav.MenuItems.OfType<NavigationViewItem>().Single(item => item.Tag as string == "library:A-movies"));
            await UntilAsync(() => shell.ActiveContentReady);
            Require(shell.CurrentTag == "library:A-movies", "侧栏选择没有导航到媒体库");
            shell.OpenDetail(DetailRequest.For(fixture.Services, Series()));
            await UntilAsync(() => shell.ActiveContentReady);
            Require((nav.SelectedItem as NavigationViewItem)?.Tag as string == "library:A-movies", "详情页丢失所属媒体库高亮");
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady);
            var libraryPage = (LibraryPage)frame.Content;
            var history = frame.BackStack.Count;
            var forward = frame.ForwardStack.Count;
            var scroll = Get<ItemsView>(libraryPage, "Cards").ScrollView!;
            ResizeProbeClient(760, 650);
            await LayoutAsync(shell);
            scroll.ScrollTo(0, 120, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            await UntilAsync(() => scroll.VerticalOffset > 0);
            var offset = scroll.VerticalOffset;
            await ToggleAsync(false);
            await ToggleAsync(true);
            await LayoutAsync(shell);
            Require(ReferenceEquals(libraryPage, frame.Content) && history == frame.BackStack.Count
                && forward == frame.ForwardStack.Count && Math.Abs(offset - scroll.VerticalOffset) < 1,
                "开关侧栏丢失页面、返回记录或滚动位置");
            Require(!nav.IsPaneOpen && Math.Abs(host.ActualWidth - 48) < 1, "窄窗未自动收成图标栏");
            AssertSidebarGeometry(shell, host, true);
            DumpSidebarLayout(shell, "760 compact");
            await SaveProbeScreenAsync("sidebar-compact-760-screen.png");

            ResizeProbeClient(600, 650);
            shell.ShowAccount("用于验证窄窗的很长很长的账号名称", "名称很长的离线服务器");
            await LayoutAsync(shell);
            // The framework's real hamburger button toggles IsPaneOpen and keeps keyboard/UIA behavior.
            var hamburger = Descendants(nav).OfType<Button>().Single(button => button.Name == "TogglePaneButton");
            ((IInvokeProvider)new ButtonAutomationPeer(hamburger).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            Require(nav.IsPaneOpen, "侧栏展开按钮没有生效");
            AssertSidebarGeometry(shell, host, true);
            DumpSidebarLayout(shell, "600 expanded");
            var title = Get<StackPanel>(shell, "TitleActions");
            var account = Get<Button>(shell, "AccountButton");
            Require(Left(account, shell) >= Left(title, shell) + title.ActualWidth + 8, "最窄窗口的账号覆盖了返回按键");
            await SaveProbeScreenAsync("sidebar-expanded-600-screen.png");
            ((IInvokeProvider)new ButtonAutomationPeer(hamburger).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            AssertSidebarGeometry(shell, host, true);

            favorite = true;
            await shell.Favorites.RefreshAsync();
            Require(Get<NavigationViewItem>(shell, "SidebarFavorites").Visibility == Visibility.Visible
                && Get<Button>(shell, "FavoritesButton").Visibility == Visibility.Collapsed, "收藏未转移到侧栏");
            favorite = false;
            await shell.Favorites.RefreshAsync();
            Require(Get<NavigationViewItem>(shell, "SidebarFavorites").Visibility == Visibility.Collapsed, "空收藏没有隐藏");

            shell.ShowPlayer(true);
            await ToggleAsync(false);
            await ToggleAsync(true);
            Require(host.Visibility == Visibility.Collapsed, "播放层中切换偏好使侧栏冒出来");
            shell.ShowPlayer(false);
            Require(host.Visibility == Visibility.Visible && ReferenceEquals(libraryPage, frame.Content), "播放返回没有恢复侧栏或重建了页面");

            SettingsReset.Restore(fixture.Settings);
            await (Task)Call(page.ViewModel, "ReapplyAndReloadAsync", "离线恢复默认")!;
            Require(host.Visibility == Visibility.Collapsed, "恢复默认没有当场关闭侧栏");
            var imported = SettingsMigration.NewDefaults();
            imported.Ui.SidebarMode = true;
            imported.Ui.AnimationsEnabled = false;
            SettingsPreferences.Apply(fixture.Settings, imported);
            await (Task)Call(page.ViewModel, "ReapplyAndReloadAsync", "离线导入")!;
            Require(host.Visibility == Visibility.Visible, "导入没有当场恢复侧栏");
            await ToggleAsync(false);
            await ToggleAsync(true);

            await fixture.SelectAsync("b.invalid");
            await (Task)Call(shell, "EnterShellAsync")!;
            var switched = nav.MenuItems.OfType<NavigationViewItem>()
                .Where(item => item.Tag is string tag && tag.StartsWith("library:", StringComparison.Ordinal)).ToArray();
            Require(switched.Length == 2 && fixture.Settings.Ui.SidebarMode
                && switched.All(item => ((string)item.Tag).StartsWith("library:B-", StringComparison.Ordinal)), "切服后缺库或留下旧媒体库");
            Call(shell, "ShowSignIn", new object?[] { null });
            Require(host.Visibility == Visibility.Collapsed && Get<Button>(shell, "SettingsButton").Visibility == Visibility.Visible,
                "注销后侧栏或设置恢复入口错误");

            async Task ToggleAsync(bool enabled)
            {
                page.Visibility = Visibility.Visible;
                await LayoutAsync(page);
                var toggle = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "侧边栏模式");
                if (toggle.IsOn != enabled)
                    ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
                Require(toggle.IsOn == enabled && fixture.Settings.Ui.SidebarMode == enabled
                    && !fixture.SettingsService.HasUnsavedChanges, "设置开关没有保存并即时生效");
                if (enabled && !fixture.Session.IsSignedIn) await SaveProbeScreenAsync("sidebar-setting-screen.png");
                page.Visibility = Visibility.Collapsed;
                await LayoutAsync(shell);
            }
        }
        finally
        {
            page.Release();
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(page);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(originalAnimations);
            ThemeHost.Apply(originalTheme);
            ResizeInspect(1420, 980);
        }

        static void Select(NavigationViewItem item) =>
            ((ISelectionItemProvider)new NavigationViewItemAutomationPeer(item).GetPattern(PatternInterface.SelectionItem)).Select();
    }

    /// <summary>读数：窗宽、栏宽、面板状态与每个入口图标的可见矩形，写进日志目录供复核（不参与断言）。</summary>
    private void DumpSidebarLayout(ShellPage shell, string label)
    {
        var nav = Get<NavigationView>(shell, "Sidebar");
        var host = Get<Grid>(shell, "SidebarHost");
        var lines = new List<string>
        {
            "== " + label,
            $"window {shell.ActualWidth:F1}x{shell.ActualHeight:F1} rail={host.ActualWidth:F1} " +
            $"pane={nav.PaneDisplayMode} open={nav.IsPaneOpen}",
            $"  paneClipOffset={Translate(PaneClip(shell))}"
        };
        foreach (var item in nav.MenuItems.OfType<NavigationViewItem>()
            .Append(Get<NavigationViewItem>(shell, "SidebarSettings")))
        {
            var bounds = Bounds(item, host);
            lines.Add($"  '{item.Content}' vis={item.Visibility} rect={bounds.X:F1},{bounds.Y:F1},{bounds.Width:F1},{bounds.Height:F1}");
            if (item.Icon is { } icon)
            {
                var iconBounds = Bounds(icon, host);
                lines.Add($"    icon rect={iconBounds.X:F1},{iconBounds.Y:F1},{iconBounds.Width:F1},{iconBounds.Height:F1} " +
                    $"visible={VisibleArea(icon, host):F1}/{iconBounds.Width * iconBounds.Height:F1}");
            }
        }
        File.AppendAllLines(Path.Combine(_options.Paths.LogDirectory, "sidebar-layout.txt"), lines);
    }

    /// <summary>元素经祖先裁剪后仍可见的面积；祖先不可见、透明为零或裁剪到空即判零。
    /// 裁剪几何自身的位移也要算进去 —— 收起态那条把整栏推出去的偏移就写在这里。</summary>
    private static double VisibleArea(FrameworkElement element, UIElement root)
    {
        var rect = Bounds(element, root);
        for (DependencyObject? ancestor = element; ancestor is not null && !ReferenceEquals(ancestor, root);
            ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FrameworkElement container) continue;
            if (container.Visibility != Visibility.Visible || container.Opacity <= 0) return 0;
            rect.Intersect(Bounds(container, root));
            if (container.Clip is RectangleGeometry clip)
                rect.Intersect(Bounds(clip.Transform is { } transform ? transform.TransformBounds(clip.Rect) : clip.Rect,
                    container, root));
            if (rect.IsEmpty) return 0;
        }
        return rect.Width * rect.Height;
    }

    private static string Translate(Transform? transform) =>
        transform is CompositeTransform composite ? $"{composite.TranslateX:F1},{composite.TranslateY:F1}" : transform?.GetType().Name ?? "-";

    private static Transform? PaneClip(ShellPage shell)
    {
        foreach (var split in Descendants(Get<NavigationView>(shell, "Sidebar")).OfType<SplitView>())
            foreach (var part in Descendants(split).OfType<FrameworkElement>())
                if (part.Clip is RectangleGeometry { Transform: CompositeTransform transform }) return transform;
        return null;
    }

    private static Windows.Foundation.Rect Bounds(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static Windows.Foundation.Rect Bounds(Windows.Foundation.Rect rect, UIElement element, UIElement root) =>
        element.TransformToVisual(root).TransformBounds(rect);

    private void AssertSidebarGeometry(ShellPage shell, Grid host, bool shown)
    {
        var content = Get<Grid>(shell, "ContentHost");
        Require(Math.Abs(Left(content, shell) - (shown ? host.ActualWidth : 0)) < 1
            && Math.Abs(content.ActualWidth + Left(content, shell) - shell.ActualWidth) < 1, "侧栏与内容区重叠或出现空白");
        var title = shell.ProbeTitleActions();
        Require(title.Ok, "标题栏布局错误：" + title.Detail);
        var hit = (int)typeof(ShellSelfCheck).GetMethod("HitAt", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [_window, title.X + title.Width / 2, title.Y + title.Height / 2])!;
        Require(hit == Native.HitClient, "侧栏收放后标题栏仍把按钮当拖动区");
        var settings = Get<NavigationViewItem>(shell, "SidebarSettings");
        var at = settings.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
        Require(settings.ActualHeight > 0 && at.Y + settings.ActualHeight <= shell.ActualHeight + 1, "侧栏设置入口被裁掉");
        var nav = Get<NavigationView>(shell, "Sidebar");
        foreach (var item in nav.MenuItems.OfType<NavigationViewItem>().Append(settings)
            .Where(item => item.Visibility == Visibility.Visible))
        {
            var icon = item.Icon;
            Require(icon is { ActualWidth: > 0, ActualHeight: > 0 }, $"侧栏入口 {item.Content} 的图标没有尺寸");
            var bounds = Bounds(icon, host);
            Require(bounds.Left >= -1 && bounds.Right <= host.ActualWidth + 1
                && bounds.Top >= 0 && bounds.Bottom <= host.ActualHeight + 1,
                $"侧栏入口 {item.Content} 的图标位于可见栏外：{bounds}");
            // 只看 TransformToVisual 的矩形是假绿：SplitView 的紧凑栏会把整块面板用裁剪几何推出栏外，
            // 元素位置和尺寸都照旧。这里按裁剪后的可见面积判断，图标必须整块留在栏内。
            var area = bounds.Width * bounds.Height;
            Require(VisibleArea(icon, host) >= area - 0.5,
                $"侧栏入口 {item.Content} 的图标被裁到栏外：可见 {VisibleArea(icon, host):F1}/{area:F1} 平方像素");
        }
    }

    private static double Left(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).X;

}
