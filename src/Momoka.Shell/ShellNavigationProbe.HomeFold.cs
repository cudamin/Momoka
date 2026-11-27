using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 整宽主页的自动档保护真实继续观看季集行：文字尚未裁掉、距视口下沿只剩 8 DIP 就上移。
    /// 两个幅宽只作布局输入，每次从真实文字线换算客户区高度，不把某组窗口尺寸当成上移／下移门槛。
    /// </summary>
    private async Task HomeFoldThresholdAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Ui.EnableImmersive = true;
        fixture.Settings.Ui.CarouselLayout = CarouselLayout.Full;
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
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries, librariesFirst: true));
        foreach (var row in fixture.Settings.Ui.HomeRows)
            row.Visible = row.Key is HomeLayout.Libraries or HomeLayout.Resume;
        var picture = await FixtureImageAsync(1280, 720);
        var items = Enumerable.Range(0, 8).Select(index => new EmbyItem
        {
            Id = "fold-episode-" + index,
            Name = "第" + (index + 1) + "集",
            SeriesName = "匿名剧集 " + (index + 1),
            ParentIndexNumber = 1,
            IndexNumber = index + 1,
            Overview = "剧情说明用于验证轮播字块与压上来的媒体库之间留出的空当。",
            Type = EmbyItemType.Episode,
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
                && home.ViewModel.LibraryShelf is { Cards.Count: > 0 }
                && home.HomeRows.OfType<CardShelf>().Any(shelf => shelf.HomeKey == HomeLayout.Resume));
            await LayoutAsync(home);

            var banner = Get<HomeBanner>(home, "Banner");
            var overlay = Get<Border>(home, "LibraryOverlay");
            var scroller = Get<ScrollView>(home, "Scroller");
            var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
            Require(banner.Layout == CarouselLayout.Full, "自动裁切夹具没有启用整宽轮播");
            var plan = fixture.Settings.Ui.HomeRows.Where(row => row.Visible).Select(row => row.Key).ToArray();
            Require(plan.SequenceEqual(new[] { HomeLayout.Libraries, HomeLayout.Resume }),
                "自动裁切夹具必须明确把媒体库排在继续观看之前");

            double CaptionLine()
            {
                var resumeIndex = Enumerable.Range(0, home.HomeRows.Count)
                    .FirstOrDefault(index => home.HomeRows[index] is CardShelf { HomeKey: HomeLayout.Resume }, -1);
                Require(resumeIndex >= 0, "继续观看匿名货架未生成");
                var row = repeater.TryGetElement(resumeIndex) as FrameworkElement
                    ?? throw new InvalidOperationException("继续观看匿名货架未实现");
                var cards = HomeFoldMotion.Find<ShelfStrip>(row)
                    ?? throw new InvalidOperationException("继续观看匿名卡片条带未实现");
                var measured = 0;
                cards.ForEachRealizedCard(card =>
                {
                    var line = Get<TextBlock>(card, "SubLine");
                    Require(line.Visibility == Visibility.Visible && line.ActualHeight > 0
                        && line.Text.StartsWith("S01E", StringComparison.Ordinal),
                        "继续观看夹具没有真实可见的季集文字行");
                    measured++;
                });
                Require(measured > 0, "继续观看没有可测的已实现季集卡片");
                return HomeFoldThresholdProbeContract.CaptionLine(
                    ((double Line, bool HasNext)?)Call(home, "MeasureCoversLine"));
            }

            foreach (var width in new[] { 760, 1100 })
            {
                ResizeProbeClient(width, 1000);
                foreach (var on in new[] { false, true })
                {
                    var placed = false;
                    for (var attempt = 0; attempt < 6; attempt++)
                    {
                        await LayoutAsync(home);
                        Call(home, "UpdateLibraryOverlay");
                        await Task.Delay(700);
                        await LayoutAsync(home);
                        var line = CaptionLine();
                        if (HomeFoldThresholdProbeContract.AtTarget(line, scroller.ActualHeight, on))
                        {
                            HomeFoldThresholdProbeContract.RequirePlacement(line, scroller.ActualHeight, on,
                                home.ViewModel.LibraryOnBanner, overlay.Visibility == Visibility.Visible);
                            placed = true;
                            break;
                        }
                        ResizeProbeClient(width, HomeFoldThresholdProbeContract.ClientHeight(
                            _root.ActualHeight, scroller.ActualHeight, line, on));
                    }
                    Require(placed, $"宽 {width} 的真实字幕裁切边界在有限布局校正后仍未达到（上移 {on}）");
                    // 等两阶段换手和防抖都结束，再重读同一条默认布局字幕线，不能只验动画途中。
                    await Task.Delay(700);
                    await LayoutAsync(home);
                    HomeFoldThresholdProbeContract.RequirePlacement(CaptionLine(), scroller.ActualHeight, on,
                        home.ViewModel.LibraryOnBanner, overlay.Visibility == Visibility.Visible);
                    if (on)
                    {
                        var infoBottom = banner.InfoBlock.TransformToVisual(banner)
                            .TransformPoint(new Point(0, banner.InfoBlock.ActualHeight)).Y;
                        var overlayTop = overlay.TransformToVisual(banner).TransformPoint(new Point(0, 0)).Y;
                        Require(overlayTop - infoBottom >= 0,
                            $"压上来的媒体库盖住了轮播字块（字块底 {infoBottom:0}、宿主顶 {overlayTop:0}）");
                    }
                    if (width == 760)
                        await SaveProbeScreenAsync(on ? "home-fold-on-screen.png" : "home-fold-room-below-screen.png");
                }
            }
        }
        finally
        {
            _root.Children.Remove(frame);
        }
    }
}

/// <summary>仅供匿名原生探针换算尺寸和验读数；不替主页决定位置。</summary>
internal static class HomeFoldThresholdProbeContract
{
    internal static double CaptionLine((double Line, bool HasNext)? site) =>
        site is { HasNext: true } measured && double.IsFinite(measured.Line) && measured.Line > 0
            ? measured.Line
            : throw new InvalidOperationException("匿名主页没有可用的继续观看字幕裁切线");

    internal static bool AtTarget(double line, double viewport, bool on)
    {
        CaptionLine((line, true));
        if (!double.IsFinite(viewport) || viewport <= 0)
            throw new InvalidOperationException("匿名主页视口裁切边界无效");
        return on
            ? line < viewport && line + HomeFold.CaptionClearance >= viewport
            : line + HomeFold.CaptionClearance < viewport;
    }

    internal static int ClientHeight(double client, double viewport, double line, bool on)
    {
        AtTarget(line, viewport, on);
        var wanted = line + HomeFold.CaptionClearance + (on ? -HomeFold.CaptionClearance / 2 : 24);
        var height = Math.Ceiling(client + wanted - viewport);
        return double.IsFinite(client) && client > 0 && double.IsFinite(height) && height > 0 && height <= int.MaxValue
            ? (int)height
            : throw new InvalidOperationException("匿名主页无法从实测字幕线换算客户区高度");
    }

    internal static void RequirePlacement(double line, double viewport, bool on, bool actualOn, bool overlayVisible)
    {
        if (!AtTarget(line, viewport, on) || actualOn != on || overlayVisible != on)
            throw new InvalidOperationException($"季集文字行裁切状态不符（字幕底 {line:0.0}、视口 {viewport:0.0}、"
                + $"提前 {HomeFold.CaptionClearance:0} DIP、预期上移 {on}、实际 {actualOn}、宿主可见 {overlayVisible}）");
    }
}
