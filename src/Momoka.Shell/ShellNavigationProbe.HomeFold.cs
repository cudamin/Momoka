using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 矮窗档的判定线（2026-11-13 用户报「这都重叠了媒体库还不下移」）：窗口 1064×761（页面宽 807、视口 744、
    /// 轮播带宽 758）时，媒体库那一排整排都放得进第一屏，旧判定线（看下面第二排的封面顶）却还把它压上圆角轮播，
    /// 播放／详情键离压上来的卡片只剩几个像素。线改成媒体库整排自己的下沿之后，同一窗口留在轮播下方；窗口矮到
    /// 整排被第一屏裁掉才压上，压上档里轮播字块与那一排之间还得留出空当。
    /// </summary>
    private async Task HomeFoldThresholdAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Ui.EnableImmersive = false;
        await fixture.SelectAsync();
        var libraries = Enumerable.Range(0, 3).Select(index => new EmbyItem
        {
            Id = "fold-library-" + index,
            Name = "离线媒体库 " + (index + 1),
            Type = EmbyItemType.CollectionFolder,
            ImageTags = new() { ["Primary"] = "poster" }
        }).ToArray();
        foreach (var library in libraries) fixture.Session.Capture().Bind(library);
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.Auto;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries));
        foreach (var row in fixture.Settings.Ui.HomeRows)
            row.Visible = row.Key is HomeLayout.Libraries or HomeLayout.Resume;
        var picture = await FixtureImageAsync(1280, 720);
        var items = Enumerable.Range(0, 8).Select(index => new EmbyItem
        {
            Id = "fold-film-" + index,
            Name = "匿名影片 " + (index + 1),
            Overview = "剧情说明用于验证轮播字块与压上来的媒体库之间留出的空当。",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(items));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = items, TotalRecordCount = items.Length }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries, null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.ViewModel.Slides.Count > 0
                && home.ViewModel.LibraryShelf is { Cards.Count: > 0 } && home.HomeRows.Count >= 2);
            await LayoutAsync(home);

            var banner = Get<HomeBanner>(home, "Banner");
            var overlay = Get<Border>(home, "LibraryOverlay");
            var scroller = Get<ScrollView>(home, "Scroller");

            // 用户那一档的页面宽高（侧栏展开 256 时窗口 1064 → 页面 807）。探针里页面就是整幅客户区。
            ResizeProbeClient(807, 761);
            await LayoutAsync(home);
            await UntilAsync(() => !home.ViewModel.LibraryOnBanner && overlay.Visibility == Visibility.Collapsed);
            await Task.Delay(700);
            await LayoutAsync(home);

            var line = ((ValueTuple<double, bool>)Call(home, "MeasureCoversLine")!).Item1;
            Require(scroller.ActualHeight > line,
                $"媒体库整排已经放得下，判定线却还在第一屏外（线 {line:0}、视口 {scroller.ActualHeight:0}）");
            Require(!home.ViewModel.LibraryOnBanner, "媒体库放得下的窗口仍压上轮播");
            Require(overlay.Visibility == Visibility.Collapsed, "回默认档还留着轮播上的媒体库宿主");
            var band = HomeCarousel.Height(scroller.ActualHeight, banner.ActualWidth);
            Require(Math.Abs(banner.ActualHeight - band) <= 1.5,
                $"这一档的轮播不是设计形状（{banner.ActualHeight:0} 对 {band:0}）");
            await SaveProbeScreenAsync("home-fold-room-below-screen.png");

            // 矮到媒体库整排被第一屏裁掉：这一档要压上轮播，字块与那一排之间不能贴上。
            ResizeProbeClient(807, 560);
            await UntilAsync(() => home.ViewModel.LibraryOnBanner && overlay.Visibility == Visibility.Visible);
            await Task.Delay(700);
            await LayoutAsync(home);

            var foldedLine = ((ValueTuple<double, bool>)Call(home, "MeasureCoversLine")!).Item1;
            Require(foldedLine >= scroller.ActualHeight - 0.5,
                $"压上档的判定线不在第一屏外（线 {foldedLine:0}、视口 {scroller.ActualHeight:0}）");
            var infoBottom = banner.InfoBlock.TransformToVisual(banner)
                .TransformPoint(new Point(0, banner.InfoBlock.ActualHeight)).Y;
            var overlayTop = overlay.TransformToVisual(banner).TransformPoint(new Point(0, 0)).Y;
            Require(overlayTop - infoBottom >= 0,
                $"压上来的媒体库盖住了轮播字块（字块底 {infoBottom:0}、宿主顶 {overlayTop:0}）");
            await SaveProbeScreenAsync("home-fold-on-screen.png");
        }
        finally
        {
            _root.Children.Remove(frame);
        }
    }
}
