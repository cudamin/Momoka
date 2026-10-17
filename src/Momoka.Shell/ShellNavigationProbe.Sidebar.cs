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
using Momoka.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

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
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities);
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

            var moviesTag = HomeLayout.LibraryKey(shell.LibraryViews.Single(view => view.Id == "A-movies"));
            Select(nav.MenuItems.OfType<NavigationViewItem>().Single(item => item.Tag as string == moviesTag));
            await UntilAsync(() => shell.ActiveContentReady);
            Require(shell.CurrentTag == moviesTag, "侧栏选择没有导航到媒体库");
            shell.OpenDetail(DetailRequest.For(fixture.Services, Series()));
            await UntilAsync(() => shell.ActiveContentReady);
            Require((nav.SelectedItem as NavigationViewItem)?.Tag as string == moviesTag, "详情页丢失所属媒体库高亮");
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady);
            var libraryPage = (LibraryPage)frame.Content;
            var history = frame.BackStack.Count;
            var forward = frame.ForwardStack.Count;
            var scroll = Get<ItemsView>(libraryPage, "CardWall").ScrollView!;
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
            // 收起那一档先读一格底色：栏 48 宽，x=24 落在它的中线上；y 取七成高，两档都落在栏的空白上
            // （条目到 236 为止、账号那一组从 613 起）。
            var railSpot = (X: 24.0, Y: shell.ActualHeight * 0.7);
            var railColor = await SidebarColorAsync(shell, railSpot.X, railSpot.Y);
            // 2026-10-13 起开合由标题栏左上角那颗导航开关负责（框架自带那颗收掉了），它同时把菜单项
            // 往上顶到标题栏下沿。走的仍是 UIA 的 Invoke，和用户按下去是同一条路。
            var toggle = Get<Button>(shell, "NavToggleButton");
            Require(toggle.Visibility == Visibility.Visible, "侧栏在屏上时导航开关没出现");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            Require(nav.IsPaneOpen, "侧栏展开按钮没有生效");
            AssertSidebarGeometry(shell, host, true);
            // 展开前后同一格必须同色，而且就是主题的面色（2026-10-14 用户：「侧边栏展开后不要变色」）。
            // 只比属性证明不了屏上画了什么 —— 这里读的是元素树自己渲染出来的那一格像素。
            var expandedColor = await SidebarColorAsync(shell, railSpot.X, railSpot.Y);
            var surface = ThemeHost.ToColor(ThemeHost.Current.Colors.Surface);
            Require(railColor == expandedColor,
                $"侧栏展开后底色变了：收起时 {railColor}、展开后 {expandedColor}");
            Require(expandedColor == $"#{surface.R:X2}{surface.G:X2}{surface.B:X2}",
                $"侧栏底色不是主题的面色：{expandedColor} 对 #{surface.R:X2}{surface.G:X2}{surface.B:X2}");
            DumpSidebarLayout(shell, "600 expanded");
            var title = Get<StackPanel>(shell, "TitleActions");
            var account = Get<Button>(shell, "AccountButton");
            // 账号那一组挪到左下角之后，标题栏那一排不再和它抢地方：整排必须留在标题栏里（不越出那 32），
            // 而账号必须落在窗口下半、栏的左端。
            Require(Top(title, shell) + title.ActualHeight <= HomePage.ChromeHeight + 1,
                "标题栏那一排被挤出了标题栏");
            // 账号那一组在左下角那一格里还留着 6 的内边距（AccountCluster 的 Padding），所以左沿不是 0。
            Require(Left(account, shell) <= 12 && Top(account, shell) > shell.ActualHeight / 2,
                "账号没有落在左下角");
            Require(Left(account, shell) + account.ActualWidth <= host.ActualWidth + 1,
                "账号溢出了侧边栏的宽");
            // 导航开关站在标题栏最左边（标题的位置），后退/前进紧跟在它右边。
            var backButton = Get<Button>(shell, "BackButton");
            var forwardButton = Get<Button>(shell, "ForwardButton");
            Require(Left(toggle, shell) <= 20 && Left(toggle, shell) < Left(backButton, shell)
                && Top(toggle, shell) + toggle.ActualHeight <= HomePage.ChromeHeight + 1,
                "导航开关不在标题栏最左边");
            Require(Left(backButton, shell) >= Left(toggle, shell) + toggle.ActualWidth
                && Left(forwardButton, shell) >= Left(backButton, shell) + backButton.ActualWidth,
                "后退/前进没有排在导航开关右边");
            await SaveProbeScreenAsync("sidebar-expanded-600-screen.png");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            AssertSidebarGeometry(shell, host, true);
            DumpSidebarLayout(shell, "600 collapsed");
            await SaveProbeScreenAsync("sidebar-collapsed-600-screen.png");

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
            await UntilAsync(() => shell.ActiveContentReady && shell.LibraryViews.Count == 4);
            var switched = nav.MenuItems.OfType<NavigationViewItem>()
                .Where(item => item.Tag is string tag && tag.StartsWith("library:", StringComparison.Ordinal)).ToArray();
            Require(switched.Length == 4 && fixture.Settings.Ui.SidebarMode
                && switched.Select(item => (string)item.Tag).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(shell.LibraryViews.Select(HomeLayout.LibraryKey)), "第二个账号上线后没有保留所有来源的独立媒体库");
            Call(shell, "ShowSignIn", null, false, null);
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

    /// <summary>
    /// 栏里某一格真的画成了什么色：把外壳渲染进自己的一块位图再读那一格（<see cref="RenderTargetBitmap"/>），
    /// 不去碰桌面 —— 取样与「别人的窗口压在上面」无关，也没有取不到屏幕 DC 那一类失败。
    /// <para>
    /// 2026-10-14 用户一句「侧边栏展开后不要变色」：栏的底在「先收起、再按 ≡ 展开」这条路上会多出一层
    /// 亚克力（见 Palette.xaml 里那两颗键的注释）。读同一格比两次 —— 收起一次、展开一次 —— 才是这句要求的
    /// 直接证据；只读控件的属性说不出屏上画了什么。
    /// </para>
    /// </summary>
    private static async Task<string> SidebarColorAsync(ShellPage shell, double x, double y)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(shell);

        var buffer = await bitmap.GetPixelsAsync();
        var pixels = new byte[buffer.Length];
        DataReader.FromBuffer(buffer).ReadBytes(pixels);

        // 位图是按岛的像素尺寸给的（BGRA8、预乘），所以逻辑坐标要乘上缩放再夹进边界。
        var scale = shell.XamlRoot.RasterizationScale;
        var column = Math.Clamp((int)Math.Round(x * scale), 0, bitmap.PixelWidth - 1);
        var row = Math.Clamp((int)Math.Round(y * scale), 0, bitmap.PixelHeight - 1);
        var at = ((row * bitmap.PixelWidth) + column) * 4;
        return $"#{pixels[at + 2]:X2}{pixels[at + 1]:X2}{pixels[at]:X2}";
    }

    /// <summary>读数：窗宽、栏宽、面板状态与每个入口图标的可见矩形，写进日志目录供复核（不参与断言）。</summary>
    private void DumpSidebarLayout(ShellPage shell, string label)
    {
        var nav = Get<NavigationView>(shell, "Sidebar");
        var host = Get<Grid>(shell, "SidebarHost");
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot) as FrameworkElement;
        var lines = new List<string>
        {
            "== " + label,
            $"window {shell.ActualWidth:F1}x{shell.ActualHeight:F1} rail={host.ActualWidth:F1} " +
            $"pane={nav.PaneDisplayMode} open={nav.IsPaneOpen}",
            $"  paneClipOffset={Translate(PaneClip(shell))}",
            $"  focus={(focused is null ? "无" : string.IsNullOrEmpty(focused.Name) ? focused.GetType().Name : focused.Name)}"
                + $"/{Get<Button>(shell, "AccountButton").FocusState}"
        };
        foreach (var item in nav.MenuItems.OfType<NavigationViewItem>())
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

        // 2026-10-13 起账号和设置不在 NavigationView 里，是左下角那一组里的两颗按钮。图标栏那一档按钮里
        // 只剩一颗字形，所以字形本身也读一遍：它被内容格挤窄的时候，按钮的矩形看着照旧是对的。
        foreach (var (entry, button, glyph) in new (string, Button, FontIcon)[]
        {
            ("账号", Get<Button>(shell, "AccountButton"), Get<FontIcon>(shell, "AccountGlyph")),
            ("设置", Get<Button>(shell, "SidebarSettingsButton"), Get<FontIcon>(shell, "SidebarSettingsGlyph"))
        })
        {
            var bounds = Bounds(button, host);
            lines.Add($"  [{entry}] vis={button.Visibility} rect={bounds.X:F1},{bounds.Y:F1},{bounds.Width:F1},{bounds.Height:F1}");
            if (glyph.Visibility != Visibility.Visible) continue;
            var glyphBounds = Bounds(glyph, host);
            lines.Add($"    glyph rect={glyphBounds.X:F1},{glyphBounds.Y:F1},{glyphBounds.Width:F1},{glyphBounds.Height:F1} " +
                $"visible={VisibleArea(glyph, host):F1}/{glyphBounds.Width * glyphBounds.Height:F1}");
        }

        // 账号那两格字的矩形也读一遍：两行那一档排错了只是「一行小字」，尺寸上看得出来的是行距和左沿。
        foreach (var (entry, text) in new (string, TextBlock)[]
        {
            ("名字", Get<TextBlock>(shell, "AccountUser")),
            ("服务器", Get<TextBlock>(shell, "AccountServer")),
            ("·", Get<TextBlock>(shell, "AccountDot"))
        })
        {
            if (text.Visibility != Visibility.Visible) { lines.Add($"  ({entry}) 收着"); continue; }
            var bounds = Bounds(text, host);
            lines.Add($"  ({entry}) rect={bounds.X:F1},{bounds.Y:F1},{bounds.Width:F1},{bounds.Height:F1} " +
                $"visible={VisibleArea(text, host):F1}/{bounds.Width * bounds.Height:F1}");
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

        // 导航开关的字形要和栏里那几项的图标落在同一条竖线上（用户 2026-10-13：「关闭导航没和下面对齐」）。
        // 判 3 像素：栏收成 48 宽那一档，框架模板把图标中线放在 26 上（展开时是 24），而缩进只有一个数，
        // 两档里必有一档差 2 像素 —— 这里钉的是「不再差 15 像素」，不是「两档都精确到 0」。
        if (shown
            && Get<Button>(shell, "NavToggleButton") is { Visibility: Visibility.Visible } toggle
            && toggle.Content is FrameworkElement glyph
            && Get<NavigationViewItem>(shell, "SidebarHome").Icon is { } navIcon
            && glyph.ActualWidth > 0 && navIcon.ActualWidth > 0)
        {
            var toggleCentre = Left(glyph, shell) + glyph.ActualWidth / 2;
            var navCentre = Left(navIcon, shell) + navIcon.ActualWidth / 2;
            Require(Math.Abs(toggleCentre - navCentre) <= 3,
                $"导航开关的字形没和栏里那几项对齐：中线 {toggleCentre:0.#} 对 {navCentre:0.#}");
        }
        var title = shell.ProbeTitleActions();
        Require(title.Ok, "标题栏布局错误：" + title.Detail);
        var hit = (int)typeof(ShellSelfCheck).GetMethod("HitAt", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [_window, title.X + title.Width / 2, title.Y + title.Height / 2])!;
        Require(hit == Native.HitClient, "侧栏收放后标题栏仍把按钮当拖动区");
        var settings = Get<Button>(shell, "SidebarSettingsButton");
        var at = settings.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
        Require(settings.ActualHeight > 0 && at.Y + settings.ActualHeight <= shell.ActualHeight + 1, "侧栏设置入口被裁掉");
        Require(at.X >= -1 && at.X + settings.ActualWidth <= host.ActualWidth + 1, "侧栏设置入口跑到栏外");
        var nav = Get<NavigationView>(shell, "Sidebar");
        foreach (var item in nav.MenuItems.OfType<NavigationViewItem>().Where(item => item.Visibility == Visibility.Visible))
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

        // 图标栏那一档，账号和设置各只剩一颗字形：字形必须整块留在栏内、中线要和栏里那几项对齐。从前按钮的
        // 内容格只有 12 宽（栏 48 − 组内边距 6×2 − 列间距 4 − 按钮内边距 10×2），16 宽的字形右边被裁掉 3 像素
        //（用户 2026-10-13：「收起侧边栏时候会被遮挡」）。被内容格挤窄的元素连自己的矩形都跟着变小，所以
        // 「宽」和「裁剪后可见面积」两条都要量。
        if (!nav.IsPaneOpen && Get<NavigationViewItem>(shell, "SidebarHome").Icon is { ActualWidth: > 0 } railIcon)
        {
            var railCentre = Left(railIcon, host) + railIcon.ActualWidth / 2;
            foreach (var (entry, entryGlyph) in new (string, FontIcon)[]
            {
                ("账号", Get<FontIcon>(shell, "AccountGlyph")),
                ("设置", Get<FontIcon>(shell, "SidebarSettingsGlyph"))
            })
            {
                Require(entryGlyph.Visibility == Visibility.Visible && entryGlyph.ActualWidth > 0,
                    $"图标栏里 {entry} 的字形没有露出来");
                Require(entryGlyph.ActualWidth >= entryGlyph.FontSize - 0.5,
                    $"图标栏里 {entry} 的字形被内容格挤窄：{entryGlyph.ActualWidth:F1} 对字号 {entryGlyph.FontSize:F1}");
                var glyphBounds = Bounds(entryGlyph, host);
                Require(VisibleArea(entryGlyph, host) >= glyphBounds.Width * glyphBounds.Height - 0.5,
                    $"图标栏里 {entry} 的字形被裁到栏外：可见 {VisibleArea(entryGlyph, host):F1}/{glyphBounds.Width * glyphBounds.Height:F1} 平方像素");
                var centre = Left(entryGlyph, host) + entryGlyph.ActualWidth / 2;
                // 判 3 像素：框架模板在图标栏那一档自己就有两处位置 —— 按窗宽自动收起来时图标中线是 24，
                // 从展开态按开关收起来时是 26（同一份读数见 sidebar-layout.txt 的两档）。缩进只有一个数，
                // 这里钉的是「字形落在栏的中线上、不再差十几像素」，不是「框架两档都精确到 0」。
                Require(Math.Abs(centre - railCentre) <= 3,
                    $"{entry} 的字形没和栏里那几项对齐：中线 {centre:0.#} 对 {railCentre:0.#}");
            }
        }

        // 左下角那一组里那两格字（2026-10-13 用户：「改成用户名在上服务器名在下」）：侧边栏展开那一档是两行 ——
        // 用户名在上、服务器名在下、左沿对齐在同一条竖线上，中间那个「·」收起，按钮真长到了两行那么高。
        // 排错（挤成一行、或者服务器名跑到上面）在截图里都只是「一行小字」，得逐字读才看得出来。
        if (shown && nav.IsPaneOpen && Get<Grid>(shell, "AccountText").Visibility == Visibility.Visible)
        {
            var user = Get<TextBlock>(shell, "AccountUser");
            var server = Get<TextBlock>(shell, "AccountServer");
            var accountButton = Get<Button>(shell, "AccountButton");
            Require(user.Visibility == Visibility.Visible && server.Visibility == Visibility.Visible,
                "侧边栏展开时账号那两格字没有都露出来");
            Require(Top(user, shell) + user.ActualHeight <= Top(server, shell) + 1,
                $"用户名不在服务器名上面：{Top(user, shell):0.#} 对 {Top(server, shell):0.#}");
            Require(Math.Abs(Left(user, shell) - Left(server, shell)) <= 1,
                $"两行字没有左对齐：{Left(user, shell):0.#} 对 {Left(server, shell):0.#}");
            Require(Get<TextBlock>(shell, "AccountDot").Visibility == Visibility.Collapsed,
                "两行那一档还留着名字和服务器之间那个「·」");
            Require(accountButton.ActualHeight >= user.ActualHeight + server.ActualHeight,
                $"账号那颗按钮没有长到两行：{accountButton.ActualHeight:0.#} 装不下 "
                    + $"{user.ActualHeight:0.#}+{server.ActualHeight:0.#}");
            var accountBounds = Bounds(accountButton, host);
            var serverBounds = Bounds(server, host);
            Require(serverBounds.Bottom <= accountBounds.Bottom + 1 && serverBounds.Right <= accountBounds.Right + 1,
                $"服务器名那一行被账号按钮裁掉了：{serverBounds} 对 {accountBounds}");
        }
    }

    private static double Left(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).X;

    private static double Top(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y;

    /// <summary>UIA 选中一个侧栏项 —— 和用户点它是同一条路（走 <c>SelectionChanged</c>）。</summary>
    private static void SelectSidebarItem(NavigationViewItem item) =>
        ((ISelectionItemProvider)new NavigationViewItemAutomationPeer(item).GetPattern(PatternInterface.SelectionItem)).Select();

    /// <summary>
    /// 侧边栏那两条 MoviePilot 订阅入口（2026-10-13）。判据是 <c>MoviePilotService.Available</c>，所以这条
    /// 用例用假 MoviePilot 走一遍「没连上 → 连上 → 断线」：入口的出现与收起、点开落在订阅列表页、以及断线
    /// 时那一页连同它的返回历史一起退掉。
    /// </summary>
    private async Task MoviePilotSidebarAsync()
    {
        var originalAnimations = HomeMotion.PreferenceEnabled;
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.SidebarMode = true;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var movies = Get<NavigationViewItem>(shell, "SidebarMoviePilotMovies");
        var series = Get<NavigationViewItem>(shell, "SidebarMoviePilotSeries");
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1240, 900);
            // 先排版一次：Loaded 那一拍才会按设置里的侧边栏开关把栏立起来。
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await LayoutAsync(shell);
            Require(Get<Grid>(shell, "SidebarHost").Visibility == Visibility.Visible, "侧栏没有立起来");
            Require(movies.Visibility == Visibility.Collapsed && series.Visibility == Visibility.Collapsed,
                "MoviePilot 没连上时侧栏仍显示订阅入口");

            // **先把栏展开，再连 MoviePilot** —— 应用里那条路就是这个顺序（窗宽那一拍先把栏开起来、
            // MoviePilot 的可用性随后才到），而分区标题的内容可见性是 NavigationView 按面板模式在实现
            // 那一刻算的：标题在栏收着的时候翻成 Visible，之后展开也不回头补（标题占着位置、一个字不画，
            // 2026-10-13 晚实测；「媒体库」那条标题同一个脾气）。这一档顺带就是这一帧截图要的画面。
            var nav = Get<NavigationView>(shell, "Sidebar");
            var host = Get<Grid>(shell, "SidebarHost");
            nav.IsPaneOpen = true;
            await LayoutAsync(shell);
            Require(nav.IsPaneOpen && Math.Abs(host.ActualWidth - 220) < 1, "订阅那一段没有把栏展开");

            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => movies.Visibility == Visibility.Visible && series.Visibility == Visibility.Visible);
            Require(movies.Content as string == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies)
                && series.Content as string == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries),
                "侧栏订阅入口的文案不是主页那两排的名字");
            // 两条入口 2026-10-13 晚起有了自己的一段：「订阅」那条分区标题必须和它们同进同退，而且站在
            // 「媒体库」那一段**下面**（用户：「侧边栏展开时新增订阅分区，放在媒体库下面」）。媒体库那几项是
            // 运行时插在两条标题之间的，所以只比标题的先后就够。
            var section = Get<NavigationViewItemHeader>(shell, "SidebarSubscriptionsHeader");
            Require(section.Visibility == Visibility.Visible && section.Content as string == "订阅",
                "订阅分区标题没有跟着两条入口一起出现");
            Require(nav.MenuItems.IndexOf(section) > nav.MenuItems.IndexOf(Get<NavigationViewItemHeader>(shell, "SidebarLibrariesHeader")),
                "订阅分区没有排在媒体库那一段下面");
            // 标题得真有高度 —— 「Visibility 是 Visible 可是没画出高度」在截图上只是「少了一行字」，
            // 只看 Visibility 会放过它。
            Require(section.ActualHeight > 0, $"订阅分区标题没有画出高度：{section.ActualHeight:0.#}");
            // 图标是照 MoviePilot 网页端复刻的两条 16×16 路径（场记板／带底座的电视）。这里量的是
            // 「PathIcon 在栏里真占了地方、也没被栏裁掉」—— 路径画错或尺寸不对时图标会是空的或只露出一角，
            // 而截图上一眼看不出来的是「它到底画没画出来」。
            foreach (var (entry, item) in new[] { ("电影", movies), ("电视剧", series) })
            {
                Require(item.Icon is { ActualWidth: > 0, ActualHeight: > 0 },
                    $"订阅入口「{entry}」的图标没有尺寸");
                var icon = item.Icon!;
                var bounds = Bounds(icon, host);
                Require(bounds.Left >= -1 && bounds.Right <= host.ActualWidth + 1
                    && bounds.Top >= 0 && bounds.Bottom <= host.ActualHeight + 1
                    && VisibleArea(icon, host) >= bounds.Width * bounds.Height - 0.5,
                    $"订阅入口「{entry}」的图标被裁到栏外：{bounds}");
            }
            await SaveProbeScreenAsync("sidebar-moviepilot-1240-screen.png");

            SelectSidebarItem(movies);
            await UntilAsync(() => shell.Pages.Content is MoviePilotSubscriptionsPage);
            var page = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await UntilAsync(() => page.ViewModel.IsReady);
            await LayoutAsync(page);
            Require(shell.CurrentTag == HomeLayout.MoviePilotMovies, "订阅列表页没有落在侧栏那把钥匙上");
            Require(Get<TextBlock>(page, "PageTitle").Text == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies),
                "订阅列表页的标题不对");
            Require(Get<NavigationView>(shell, "Sidebar").SelectedItem is NavigationViewItem { Tag: string tag }
                && tag == HomeLayout.MoviePilotMovies, "订阅列表页没有高亮对应的侧栏项");
            // 这一页只画它那一半：电影入口下电视剧那一排整块收着（数据两份都在，收的是版面）。
            // 两条货架带住在 MoviePilotSubscriptionsView 自己的名字域里，所以从那一层找。
            var view = Get<MoviePilotSubscriptionsView>(page, "Subscriptions");
            Require(Get<ShelfStrip>(view, "MovieSubscriptions").Visibility == Visibility.Visible
                && Get<ShelfStrip>(view, "SeriesSubscriptions").Parent is FrameworkElement hidden
                && hidden.Visibility == Visibility.Collapsed, "电影订阅页没有只画电影那一排");
            Require(Get<StackPanel>(page, "EmptyState").Visibility == Visibility.Collapsed, "有订阅时仍显示空态");
            await SaveProbeScreenAsync("sidebar-moviepilot-page-screen.png");

            SelectSidebarItem(series);
            await UntilAsync(() => shell.CurrentTag == HomeLayout.MoviePilotSeries);
            // 换目的地是**新的一页**（Frame 不缓存页面），旧那一页在 OnNavigatedFrom 里已经放开它的订阅数据 ——
            // 从旧实例读到的 ViewModel 已经换回它自己的备用属主，IsReady 永远是假。
            var seriesPage = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await UntilAsync(() => seriesPage.ViewModel.IsReady);
            await LayoutAsync(seriesPage);
            Require(Get<TextBlock>(seriesPage, "PageTitle").Text == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries),
                "换到电视剧入口后标题没跟着换");
            var seriesView = Get<MoviePilotSubscriptionsView>(seriesPage, "Subscriptions");
            Require(Get<ShelfStrip>(seriesView, "SeriesSubscriptions").Visibility == Visibility.Visible
                && Get<ShelfStrip>(seriesView, "MovieSubscriptions").Parent is FrameworkElement collapsed
                && collapsed.Visibility == Visibility.Collapsed, "电视剧订阅页没有只画电视剧那一排");

            // 断线：两条入口当场收起，那一条分区标题跟着收，这一页也退掉，返回历史里不留。
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => movies.Visibility == Visibility.Collapsed && series.Visibility == Visibility.Collapsed);
            Require(section.Visibility == Visibility.Collapsed, "两条入口收起后「订阅」分区标题还留在栏里");
            await UntilAsync(() => shell.Pages.Content is not MoviePilotSubscriptionsPage);
            Require(shell.Pages.BackStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage))
                && shell.Pages.ForwardStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage)),
                "断线后返回或前进历史里还留着订阅列表页");
            // 断线只收那两条入口，账号那一组和设置照旧站在左下角。
            var account = Get<Button>(shell, "AccountButton");
            Require(account.Visibility == Visibility.Visible
                && Left(account, shell) <= 12 && Top(account, shell) > shell.ActualHeight / 2,
                "断线后账号那一组离开了左下角");
            Require(Get<Button>(shell, "SidebarSettingsButton").Visibility == Visibility.Visible, "断线后设置入口丢了");
        }
        finally
        {
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(originalAnimations);
            ResizeInspect(1420, 980);
        }
    }

}
