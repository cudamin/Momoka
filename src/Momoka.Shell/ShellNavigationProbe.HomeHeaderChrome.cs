using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.Graphics;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeHeaderChromeAsync()
    {
        var window = _window ?? throw new InvalidOperationException("顶部收放探针缺少已创建的窗口");
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Session.TakeRestoredViews();
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
        var libraries = Enumerable.Range(0, 4).Select(index => new EmbyItem
        {
            Id = "header-chrome-library-" + index,
            Name = "匿名媒体库 " + (index + 1),
            Type = EmbyItemType.CollectionFolder,
            CollectionType = "movies"
        }).ToArray();
        foreach (var library in libraries) fixture.Session.Capture().Bind(library);
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries));
        foreach (var row in fixture.Settings.Ui.HomeRows)
            row.Visible = row.Key == HomeLayout.Libraries || libraries.Any(library => row.Key == HomeLayout.LibraryKey(library));
        var artwork = await FixtureImageAsync(1280, 720);
        var movies = Enumerable.Range(0, 24).Select(index => new EmbyItem
        {
            Id = "header-chrome-film-" + index,
            Name = "匿名影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "顶部收放探针禁止服务器写入");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(artwork) });
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Task.FromResult(Json(new { Items = libraries }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(movies));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        fixture.Actions.TitleStripChanged = shell.SetTitleStrip;
        fixture.Actions.HomeHeaderChanged = shell.RefreshHomeHeader;
        // Lend the existing anonymous HWND only; AttachWindow would attach a real player service.
        Field(shell, "_window", window);
        _root.Children.Add(shell);
        var originalTheme = ThemeHost.Current;
        var snapshots = new List<object>();
        var layouts = new List<object>();
        Exception? probeFailure = null;
        try
        {
            ResizeProbeClient(1240, 760);
            await LayoutAsync(shell);
            ReadLayout(1240, "initial-shell-layout", false);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage home && !home.ViewModel.Busy);
            Require(window.UsesCustomTitleBar, "顶部收放检查缺少真实自定义非客户区");
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeProbeClient(width, 760);
                var home = (HomePage)shell.Pages.Content;
                await LayoutAsync(home);
                ReadLayout(width, "home-layout", true);
                var scroll = Get<ScrollView>(home, "Scroller");
                await UntilAsync(() => scroll.ScrollableHeight > 400 && home.BannerShown);
                await ScrollProbeAsync(scroll, 0);
                Read("top", HomeHeaderMode.Top);
                if (width == 1240) await SaveProbeScreenAsync("home-header-chrome-top-1240-screen.png");
                var headerButton = Get<Button>(shell, "SettingsButton");
                Require(headerButton.Focus(FocusState.Programmatic), "顶部设置按钮无法作为焦点收放前件");
                await ScrollProbeAsync(scroll, 240);
                Read("down", HomeHeaderMode.Hidden);
                HomeHeaderChromeProbeReadings.FocusTransfer(240, scroll.VerticalOffset, FocusInHeader());
                Require(Within(FocusManager.GetFocusedElement(shell.XamlRoot) as DependencyObject, scroll),
                    "收起顶部后焦点没有交给当前主页滚动容器");
                if (width == 1240) await SaveProbeScreenAsync("home-header-chrome-hidden-1240-screen.png");
                await Task.Delay(150);
                Read("stopped", HomeHeaderMode.Hidden);
                await ScrollProbeAsync(scroll, 236);
                Read("reverse-jitter", HomeHeaderMode.Hidden);
                await ScrollProbeAsync(scroll, 228);
                Read("up-threshold", HomeHeaderMode.Revealed);
                Require(Get<Border>(shell, "HomeHeaderBackground").Background is AcrylicBrush,
                    "上滚背景没有使用项目原生亚克力资源");
                await SaveProbeScreenAsync($"home-header-chrome-revealed-{width}-screen.png");

                // A horizontal shelf runs its actual ScrollView event without changing the page's direction.
                var shelf = home.HomeRows.OfType<CardShelf>().First(value => value.Cards.Count == movies.Length);
                var row = (FrameworkElement)Get<ItemsRepeater>(home, "ShelfRepeater").GetOrCreateElement(home.HomeRows.IndexOf(shelf));
                await LayoutAsync(row);
                ReadLayout(width, "shelf-layout", true);
                var strip = HomeFoldMotion.Find<ShelfStrip>(row)!;
                var horizontal = Get<ScrollView>(strip, "Scroller");
                await UntilAsync(() => horizontal.ScrollableWidth > 32);
                var target = Math.Min(180, horizontal.ScrollableWidth);
                horizontal.ScrollTo(target, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                await UntilAsync(() => Math.Abs(horizontal.HorizontalOffset - target) < 1);
                Require(Math.Abs(scroll.VerticalOffset - 228) <= 1, "横向货架改变了主页纵向位置");
                Read("horizontal", HomeHeaderMode.Revealed);

                if (width == 1240)
                {
                    foreach (var theme in UiThemes.All)
                    {
                        ThemeHost.Apply(theme);
                        await LayoutAsync(shell);
                        Read("theme-" + theme.Id, HomeHeaderMode.Revealed);
                    }
                    ThemeHost.Apply(originalTheme);
                    await LayoutAsync(shell);
                    ReadLayout(width, "theme-restored", true);
                }
                fixture.Settings.Ui.EnableImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(shell);
                Read("store-mode", HomeHeaderMode.Top);
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(shell);
                Read("immersive-restored", HomeHeaderMode.Revealed);

                await ScrollProbeAsync(scroll, 300);
                Read("before-leave", HomeHeaderMode.Hidden);
                var preserved = scroll.VerticalOffset;
                shell.OpenChild(LibraryRequest.For(fixture.Services, libraries[0]));
                await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is LibraryPage);
                Read("other-page", HomeHeaderMode.Top);
                Call(shell, "GoBack");
                await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage returned
                    && !Get<bool>(returned, "_restoringHeaderScroll")
                    && Math.Abs(Get<ScrollView>(returned, "Scroller").VerticalOffset - preserved) <= 1);
                home = (HomePage)shell.Pages.Content;
                scroll = Get<ScrollView>(home, "Scroller");
                Read("history-return", HomeHeaderMode.Revealed);
                Require(Math.Abs(scroll.VerticalOffset - preserved) <= 1, "正式返回主页没有恢复深处滚动位置");
                await ScrollProbeAsync(scroll, 0);
                Read("back-to-top", HomeHeaderMode.Top);
                if (width == 760) await SaveProbeScreenAsync("home-header-chrome-top-760-screen.png");

                void Read(string stage, HomeHeaderMode expected)
                {
                    var current = shell.Pages.Content as HomePage;
                    ReadLayout(width, stage, fixture.Settings.Ui.EnableImmersive && current is not null);
                    var background = Get<Border>(shell, "HomeHeaderBackground");
                    var source = Get<InputNonClientPointerSource>(window, "_nonClient");
                    Require(Interop.Native.GetClientRect(window.Handle, out var client), "读取非客户区前无法确定窗口尺寸");
                    var scale = shell.XamlRoot.RasterizationScale;
                    var caption = Regions(NonClientRegionKind.Caption);
                    var reading = new HomeHeaderChromeReading(
                        Get<Grid>(shell, "AppTitleBar").Visibility == Visibility.Visible,
                        Get<Grid>(shell, "AccountCluster").Visibility == Visibility.Visible,
                        background.Visibility == Visibility.Visible, background.IsHitTestVisible,
                        background.ActualHeight, background.ActualWidth, shell.ActualWidth,
                        window.BrowseTitleBarHidden, window.TitleBarMetrics.Height, window.TitleBarMetrics.RightInset,
                        window.TitleBarHoles?.Count ?? 0, Regions(NonClientRegionKind.Passthrough).Length,
                        caption.Any(rect => rect.X == 0 && rect.Y == 0 && rect.Width >= client.Width && rect.Height >= 48 * scale - 1),
                        Regions(NonClientRegionKind.Minimize).Length > 0,
                        Regions(NonClientRegionKind.Maximize).Length > 0,
                        Regions(NonClientRegionKind.Close).Length > 0);
                    snapshots.Add(new
                    {
                        width,
                        stage,
                        theme = ThemeHost.Current.Id,
                        mode = (current?.HeaderMode ?? HomeHeaderMode.Top).ToString(),
                        expectedMode = expected.ToString(),
                        offset = current is null ? (double?)null : Get<ScrollView>(current, "Scroller").VerticalOffset,
                        reading,
                        caption,
                        focus = (FocusManager.GetFocusedElement(shell.XamlRoot) as FrameworkElement)?.Name
                    });
                    Require((current?.HeaderMode ?? HomeHeaderMode.Top) == expected,
                        $"主页顶部档位错误：{stage} expected={expected} actual={current?.HeaderMode}");
                    HomeHeaderChromeProbeReadings.State(expected, reading);
                    RectInt32[] Regions(NonClientRegionKind kind) => source.GetRegionRects(kind)
                        .Where(rect => rect.Width > 0 && rect.Height > 0).ToArray();
                }
            }
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                "顶部收放匿名探针产生播放或服务器写入");
        }
        catch (Exception error)
        {
            probeFailure = error;
            throw;
        }
        finally
        {
            // Retain partial observations on failure; no test can overwrite the user's real settings.
            // Every detach is attempted; the cleanup runner's outer finally always attempts global restoration.
            HomeHeaderChromeProbeCleanup.Run(
            [
                () => File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "home-header-chrome.json"),
                    JsonSerializer.Serialize(new { snapshots, layouts, plays = fixture.Actions.Plays, mediaLoaded = false, realNetwork = false },
                        new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })),
                () => Call(shell, "StopMoviePilotMonitoring"),
                () => Call(shell, "ReleaseContent"),
                () => shell.SignInRoot.Detach(),
                () => fixture.Actions.TitleStripChanged = null,
                () => fixture.Actions.HomeHeaderChanged = null,
                () => Field(shell, "_window", null),
                () => _root.Children.Remove(shell)
            ],
            [
                () => window.BrowseTitleBarHidden = false,
                () => window.PlaybackTitleBar = false,
                () => window.SetTitleBarHoles(),
                () => ThemeHost.Apply(originalTheme),
                () => ResizeProbeClient(1420, 980)
            ], probeFailure);
        }

        void ReadLayout(int requestedWidth, string stage, bool fullHome)
        {
            var clientRead = Interop.Native.GetClientRect(window.Handle, out var client);
            var reading = new HomeHeaderChromeLayoutReading(requestedWidth, clientRead, client.Width,
                Interop.Native.GetDpiForWindow(window.Handle), shell.XamlRoot.RasterizationScale,
                shell.ActualWidth, (shell.Pages.Content as HomePage)?.ActualWidth);
            layouts.Add(new { stage, fullHome, theme = ThemeHost.Current.Id, reading });
            // Record the rejected width before throwing, and validate before any width-labelled screenshot.
            HomeHeaderChromeProbeReadings.Layout(reading, fullHome);
        }

        bool FocusInHeader()
        {
            var focused = FocusManager.GetFocusedElement(shell.XamlRoot) as DependencyObject;
            return Within(focused, Get<Grid>(shell, "AppTitleBar")) || Within(focused, Get<Grid>(shell, "AccountCluster"));
        }
        static bool Within(DependencyObject? node, DependencyObject root)
        {
            for (; node is not null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, root)) return true;
            return false;
        }
    }
}
