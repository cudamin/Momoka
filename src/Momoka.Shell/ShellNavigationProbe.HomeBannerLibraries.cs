using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
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
            Name = "离线影片 " + (index + 1),
            Overview = "剧情说明用于验证轮播图的文字宽度：沿用左侧起点，右边收窄，为封面图留出更多空间。亮色和暗色的画面都应保持平滑过渡。",
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

            // 连续跨越原有800 DIP显隐界线，不能只量启动时的宽度。
            foreach (var width in new[] { 1280, 800, 760, 801, 1100 })
            {
                ResizeProbeClient(width, 760);
                await LayoutAsync(home);
                await UntilAsync(() => overlay.Visibility == Visibility.Visible
                    && Math.Abs(overlay.Margin.Right - (width >= 800 ? 244 : 36)) < 1);
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
            banner.ApplyDwell(HomeCarousel.MaxDwellSeconds);
            foreach (var width in new[] { 1280, 1100 })
            {
                ResizeProbeClient(width, 760);
                await LayoutAsync(home);
                var info = Get<StackPanel>(banner, "Info");
                var synopsis = Get<Grid>(banner, "SynopsisRow");
                var fade = Get<Border>(banner, "Fade");
                await UntilAsync(() => synopsis.Visibility == Visibility.Visible && synopsis.ActualWidth > 0
                    && banner.Current?.Picture is not null && banner.FrontLayer.Opacity >= 0.99
                    && synopsis.Opacity >= 0.99 && Get<Grid>(banner, "TitleRow").Opacity >= 0.99
                    && (Get<Grid>(banner, "CaptionRow").Visibility != Visibility.Visible
                        || Get<Grid>(banner, "CaptionRow").Opacity >= 0.99));
                Require(Math.Abs(synopsis.ActualWidth - info.MaxWidth * 0.95) < 1,
                    $"剧情说明没有单独向左收窄5%：Info实际/上限={info.ActualWidth}/{info.MaxWidth}，简介实际/指定={synopsis.ActualWidth}/{synopsis.Width}，窗口={banner.ActualWidth}");
                Require(Math.Abs(Bounds(synopsis).Left - Bounds(info).Left) < 1,
                    "剧情说明收窄时改变了左侧起点");
                var finalWidth = synopsis.Width;
                var finalFade = fade.Width;
                double previousWidth;
                try
                {
                    // 同一帧图和同一段文字恢复任务前排版，验证收窄的实际效果；只改变匿名夹具并在finally恢复。
                    synopsis.Width = double.NaN;
                    synopsis.HorizontalAlignment = HorizontalAlignment.Stretch;
                    fade.Width = Math.Min(banner.ActualWidth, 60 + info.MaxWidth + 96);
                    await LayoutAsync(home);
                    previousWidth = synopsis.ActualWidth;
                    await SaveProbeScreenAsync($"home-banner-extension-{width}-before-screen.png");
                }
                finally
                {
                    synopsis.Width = finalWidth;
                    synopsis.HorizontalAlignment = HorizontalAlignment.Left;
                    fade.Width = finalFade;
                    await LayoutAsync(home);
                }
                Require(synopsis.ActualWidth < previousWidth - 1, "剧情说明没有比原排版收窄");
                Require(Math.Abs(fade.ActualWidth - Math.Min(banner.ActualWidth, 60 + info.MaxWidth + 96 + banner.ActualWidth * 0.05)) < 1,
                    "左侧阅读渐变没有延长");
                var picture = banner.FrontPicture as Microsoft.UI.Xaml.Media.Imaging.BitmapImage;
                Require(picture is { PixelWidth: 1472, PixelHeight: 720 }, "轮播位图没有保留15%左侧扩边");
                readings.Add($"{width}/synopsis：字块={info.MaxWidth:0.0}，简介={previousWidth:0.0}→{synopsis.ActualWidth:0.0}，渐变={fade.ActualWidth:0.0}，位图={picture!.PixelWidth}×{picture.PixelHeight}");
                await SaveProbeScreenAsync($"home-banner-extension-{width}-screen.png");
            }
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
        }
        finally
        {
            File.WriteAllLines(System.IO.Path.Combine(_options.Paths.LogDirectory, "home-banner-library-room.txt"), readings);
            home?.Release();
            _root.Children.Remove(frame);
            ResizeProbeClient(1420, 980);
        }
    }
}
