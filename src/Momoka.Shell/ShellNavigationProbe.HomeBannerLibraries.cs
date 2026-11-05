using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Interop;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeBannerLibrariesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var libraries = Enumerable.Range(0, 10).Select(index => new EmbyItem
        {
            Id = "banner-library-" + index,
            Name = "离线媒体库 " + (index + 1),
            Type = EmbyItemType.CollectionFolder,
            ImageTags = new() { ["Primary"] = "poster" }
        }).ToArray();
        foreach (var library in libraries) fixture.Session.Capture().Bind(library);
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.CarouselCount = 10;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.OnCarousel;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries));
        foreach (var row in fixture.Settings.Ui.HomeRows)
            row.Visible = row.Key is HomeLayout.Libraries or HomeLayout.Resume;
        var image = await FixtureImageAsync(1280, 720);
        var items = Enumerable.Range(0, 10).Select(index => new EmbyItem
        {
            Id = "banner-movie-" + index,
            Name = index == 0 ? "离线长片名：在异世界相遇的我们与尚未结束的旅程" : "离线影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(items));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = items, TotalRecordCount = items.Length }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        var readings = new List<string>();
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var originalPosition = appWindow.Position;
        var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        _root.Children.Add(frame);
        HomePage? home = null;
        try
        {
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries, null));
            home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.ViewModel.Slides.Count == 10
                && home.ViewModel.LibraryShelf?.Cards.Count == 10);
            await LayoutAsync(home);
            var banner = Get<HomeBanner>(home, "Banner");
            var overlay = Get<Border>(home, "LibraryOverlay");
            var status = Get<StackPanel>(banner, "SlideStatus");
            var counter = Get<TextBlock>(banner, "SlideCounter");
            var countdown = Get<Border>(banner, "CountdownBar");

            foreach (var (width, height, expectedBand) in new[] { (2560, 1392, 640), (1132, 1084, 484) })
            {
                ResizeProbeClient(width, height);
                await LayoutAsync(home);
                await UntilAsync(() => Math.Abs(banner.ActualHeight - expectedBand) < 1);
                // 改客户区尺寸不会把原先居中的窗口搬回来。按实测客户区原点校正，
                // 宽屏截图完整落在所选屏幕上；保留像素归属守卫，不把屏外区域当成证据。
                Require(Native.GetClientRect(_window.Handle, out var client), "首页探针无法读取客户区");
                Require(client.Width <= workArea.Width && client.Height <= workArea.Height,
                    "当前屏幕无法容纳首页宽屏验证尺寸");
                var clientOrigin = new NativePoint();
                Require(Native.ClientToScreen(_window.Handle, ref clientOrigin), "首页探针无法定位客户区");
                appWindow.Move(new Windows.Graphics.PointInt32(
                    appWindow.Position.X + workArea.X - clientOrigin.X,
                    appWindow.Position.Y + workArea.Y - clientOrigin.Y));
                _window.Activate();
                await LayoutAsync(home);
                Require(banner.ActualHeight <= 640 && Bounds(Get<Grid>(home, "Overlay")).Top < height,
                    "宽屏横幅过高，后续货架没有进入第一屏");
                Require(Get<TextBlock>(banner, "TitleText").FontSize == (double)Application.Current.Resources["EgDisplayFontSize"],
                    "常规窗口标题未恢复原来的显示刻度");
                readings.Add($"{width}×{height}：横幅高={banner.ActualHeight:0.0}，后续货架顶={Bounds(Get<Grid>(home, "Overlay")).Top:0.0}");
                await SettleBannerForCaptureAsync();
                await SaveProbeScreenAsync($"home-banner-libraries-{width}-wide-screen.png");
            }

            // 连续跨越紧凑布局760和读数800 DIP界线，不能只量启动时的宽度。
            foreach (var width in new[] { 1280, 800, 760, 759, 600, 760, 801, 1100 })
            {
                ResizeProbeClient(width, width < 760 ? 560 : 760);
                await LayoutAsync(home);
                if (width < 760)
                {
                    await UntilAsync(() => !home.ViewModel.LibraryOnBanner && overlay.Visibility == Visibility.Collapsed);
                    Require(fixture.Settings.Ui.HomeLibraryPlacement == HomeLibraryPlacement.OnCarousel,
                        "紧凑窗口改写了媒体库位置偏好");
                    var libraryIndex = home.HomeRows.ToList().FindIndex(row => ReferenceEquals(row, home.ViewModel.LibraryShelf));
                    Require(libraryIndex >= 0, "紧凑窗口没有把媒体库恢复到普通货架");
                    var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
                    var libraryRow = (FrameworkElement)repeater.GetOrCreateElement(libraryIndex);
                    await LayoutAsync(libraryRow);
                    Require(Bounds(libraryRow).Top >= Bounds(banner).Bottom,
                        "紧凑窗口媒体库仍位于轮播图中");
                    var info = Get<StackPanel>(banner, "Info");
                    var title = Get<TextBlock>(banner, "TitleText");
                    var actions = Get<StackPanel>(banner, "Actions");
                    Require(Bounds(info).Top >= Bounds(banner).Top + HomePage.ChromeHeight,
                        "紧凑窗口字幕侵入40 DIP顶栏");
                    Require(Bounds(actions).Bottom <= Bounds(banner).Bottom + 1
                        && Bounds(actions).Right <= Bounds(banner).Right + 1,
                        "紧凑窗口操作超出横幅");
                    Require(title.FontSize == (double)Application.Current.Resources["EgHeaderFontSize"],
                        "紧凑窗口标题未使用较小的现有刻度");
                    Require(title.MaxLines == 1 && status.Visibility == Visibility.Collapsed,
                        "紧凑窗口没有收紧标题或收起读数");
                    readings.Add($"{width}/compact：字幕顶={Bounds(info).Top:0.0}，媒体库货架顶={Bounds(libraryRow).Top:0.0}，偏好保留");
                    await SettleBannerForCaptureAsync();
                    await SaveProbeScreenAsync($"home-banner-libraries-{width}-compact-screen.png");
                    continue;
                }
                await UntilAsync(() => overlay.Visibility == Visibility.Visible
                    && Math.Abs(overlay.Margin.Right - (width >= 800 ? 244 : 36)) < 1);
                Require(fixture.Settings.Ui.HomeLibraryPlacement == HomeLibraryPlacement.OnCarousel,
                    "恢复宽窗后媒体库偏好改变");
                var strip = HomeFoldMotion.Find<ShelfStrip>(overlay)
                    ?? throw new InvalidOperationException("轮播上的媒体库横排没有实现");
                await LayoutAsync(strip);
                var scroll = Get<ScrollView>(strip, "Scroller");
                Require(scroll.ScrollableWidth > 0, "多媒体库没有保留横向滚动");
                scroll.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                await UntilAsync(() => scroll.HorizontalOffset < 1);
                await LayoutAsync(strip);
                CheckRoom(width, "start", strip);
                if (width == 1280)
                {
                    // 同一判据必须拒绝遮挡；只移动临时夹具的视口，不放宽正式检查。
                    var transform = strip.RenderTransform;
                    var rejected = false;
                    try
                    {
                        strip.RenderTransform = new TranslateTransform { X = 24 };
                        try { CheckRoom(width, "injected-overlap", strip); }
                        catch (InvalidOperationException error) when (error.Message == "媒体库实际滚动视口超出避让边界")
                        {
                            rejected = true;
                        }
                    }
                    finally { strip.RenderTransform = transform; }
                    Require(rejected, "避让检查没有拒绝旧布局的遮挡");
                    CheckRoom(width, "recovered-overlap", strip);
                }
                await SaveProbeScreenAsync($"home-banner-libraries-{width}-start-screen.png");

                // 通过已有翻页按钮走真实滚动路径，不点击媒体卡片本体。
                Field(strip, "_hover", true);
                Call(strip, "SyncArrows");
                var next = Get<Button>(strip, "Next");
                await UntilAsync(() => next.Visibility == Visibility.Visible && next.IsHitTestVisible);
                ((IInvokeProvider)new ButtonAutomationPeer(next).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => scroll.HorizontalOffset > 1);
                strip.FocusOn(libraries.Length - 1);
                await UntilAsync(() => scroll.HorizontalOffset >= scroll.ScrollableWidth - 1);
                await LayoutAsync(strip);
                CheckRoom(width, "end", strip);
                var last = Descendants(strip).OfType<PosterCard>().Single(card => card.Card?.Item.Id == libraries[^1].Id);
                Require(Bounds(last).Left >= Bounds(scroll).Left - 1 && Bounds(last).Right <= Bounds(scroll).Right + 1,
                    "媒体库横排末项没有完整进入视口");
                await SaveProbeScreenAsync($"home-banner-libraries-{width}-end-screen.png");
                Field(strip, "_hover", false);
                Call(strip, "SyncArrows");
            }

            // 单张与多张之间不改变带宽，也必须释放和恢复读数占用的空间。
            var slides = home.ViewModel.Slides;
            home.ViewModel.Slides = [slides[0]];
            await LayoutAsync(home);
            await UntilAsync(() => status.Visibility == Visibility.Collapsed && Math.Abs(overlay.Margin.Right - 36) < 1);
            home.ViewModel.Slides = slides;
            await LayoutAsync(home);
            await UntilAsync(() => status.Visibility == Visibility.Visible && Math.Abs(overlay.Margin.Right - 244) < 1);
            CheckRoom(1100, "restored-slides", HomeFoldMotion.Find<ShelfStrip>(overlay)!);

            fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
            Call(home, "UpdateLibraryOverlay");
            await UntilAsync(() => !home.ViewModel.LibraryOnBanner && overlay.Visibility == Visibility.Collapsed);
            fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.OnCarousel;
            Call(home, "UpdateLibraryOverlay");
            await UntilAsync(() => home.ViewModel.LibraryOnBanner && overlay.Visibility == Visibility.Visible);
            await LayoutAsync(home);
            CheckRoom(1100, "restored-placement", HomeFoldMotion.Find<ShelfStrip>(overlay)!);
            Require(fixture.Actions.Plays == 0, "媒体库避让检查意外触发播放");
            void CheckRoom(int width, string stage, ShelfStrip strip)
            {
                Require(home.ViewModel.LibraryOnBanner, "媒体库没有按固定轮播档显示");
                var visible = width >= 800;
                Require(status.Visibility == (visible ? Visibility.Visible : Visibility.Collapsed), "轮播读数显隐界线改变");
                if (!visible)
                {
                    Require(Math.Abs(overlay.Margin.Right - 36) < 1, "读数收起后没有恢复媒体库视口");
                    readings.Add($"{width}/{stage}：读数收起，右边距={overlay.Margin.Right:0.0}");
                    return;
                }
                var libraryBounds = Bounds(overlay);
                var stripBounds = Bounds(strip);
                var statusBounds = Bounds(status);
                Require(statusBounds.Left - libraryBounds.Right >= 23,
                    "媒体库覆盖层遮住轮播读数或进度条");
                Require(statusBounds.Left - stripBounds.Right >= 23,
                    "媒体库实际滚动视口超出避让边界");
                Require(statusBounds.Right <= Bounds(banner).Right - 59,
                    "轮播读数离开原来的右侧位置");
                Require(counter.Text.EndsWith("/  10", StringComparison.Ordinal) && countdown.ActualHeight >= 2,
                    "轮播张数或进度条缺失");
                // 在进度条下方同一横向区域取命中点，媒体库滚动层也不能挡到它。
                var point = new Point(statusBounds.Left + statusBounds.Width / 2, statusBounds.Bottom - 1);
                Require(!VisualTreeHelper.FindElementsInHostCoordinates(point, _root).Contains(strip),
                    "媒体库滚动层仍命中轮播指示器区域");
                readings.Add($"{width}/{stage}：媒体库右={libraryBounds.Right:0.0}，视口右={stripBounds.Right:0.0}"
                    + $"，读数左={statusBounds.Left:0.0}，间隔={statusBounds.Left - libraryBounds.Right:0.0}");
            }

            Rect Bounds(FrameworkElement element) => element.TransformToVisual(_root)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

            async Task SettleBannerForCaptureAsync()
            {
                // 仍让真实轮播和翻档运行；只在截图前等当前淡入/淡出结束，并留出取样时间。
                banner.ApplyDwell(60);
                await UntilAsync(() => Get<Grid>(banner, "TitleRow").Opacity >= 0.999
                    && Get<StackPanel>(banner, "Actions").Opacity >= 0.999);
                await LayoutAsync(banner);
            }
        }
        finally
        {
            File.WriteAllLines(System.IO.Path.Combine(_options.Paths.LogDirectory, "home-banner-library-room.txt"), readings);
            home?.Release();
            _root.Children.Remove(frame);
            ResizeProbeClient(1420, 980);
            appWindow.Move(originalPosition);
        }
    }
}
