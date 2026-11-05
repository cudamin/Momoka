using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryHeaderPolishAsync()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            using var fixture = CreateFixture();
            var poster = await FixtureImageAsync();
            fixture.Transport.Reply = (request, _) => Task.FromResult(kind == MediaServerKind.Jellyfin
                ? JellyfinResponse(request, poster) : ResumeResponse(request));
            await fixture.SelectAsync(kind: kind);
            var frame = new Frame();
            _root.Children.Add(frame);
            try
            {
                frame.Navigate(typeof(LibraryPage), LibraryRequest.ForRow(fixture.Services, HomeLayout.HomeRowTarget.Resume));
                var page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                foreach (var width in new[] { 1132, 760, 600 })
                {
                    ResizeProbeClient(width, 800);
                    await LayoutAsync(page);
                    var toolbar = Get<Grid>(page, "ToolbarSurface");
                    var controls = toolbar.TransformToVisual(page).TransformBounds(new(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
                    var title = Get<TextBlock>(page, "LibraryTitle");
                    Require(title.FontSize == (double)Application.Current.Resources["EgTitleFontSize"], "继续观看标题没有使用20DIP刻度");
                    Require(Math.Abs(controls.Left - Get<Grid>(page, "PageRoot").Margin.Left) <= 2, "继续观看工具条没有左对齐");
                    RequireResumeGap(page, controls.Bottom);
                    if (width == 1132)
                    {
                        var content = Get<Grid>(page, "EmbyContent");
                        var margin = content.Margin;
                        var rejected = false;
                        try
                        {
                            content.Margin = new Thickness(0, 128, 0, 0);
                            await LayoutAsync(page);
                            try { RequireResumeGap(page, controls.Bottom); }
                            catch (InvalidOperationException) { rejected = true; }
                        }
                        finally { content.Margin = margin; }
                        Require(rejected, "继续观看大空档反例未被检测");
                        await LayoutAsync(page);
                        RequireResumeGap(page, controls.Bottom);
                    }
                    await SaveProbeScreenAsync($"library-resume-header-{kind}-{width}-screen.png");
                }
                if (kind == MediaServerKind.Jellyfin) await CheckJellyfinIndexLaneAsync(frame, fixture, poster);
            }
            finally
            {
                (frame.Content as IShellContent)?.Release();
                frame.Content = null;
                _root.Children.Remove(frame);
            }

            HttpResponseMessage ResumeResponse(HttpRequestMessage request)
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/Images/", StringComparison.Ordinal))
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) };
                if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                    return Json(new { Items = new[] { Item("resume", EmbyItemType.Movie) }, TotalRecordCount = 1 });
                return FakeTransport.Standard(request);
            }
        }
    }

    private async Task CheckJellyfinIndexLaneAsync(Frame frame, Fixture fixture, byte[] poster)
    {
        var total = 36;
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal)
                && Query(request, "ParentId") == "index-lane")
            {
                var items = Enumerable.Range(0, total).Select(index => Item($"index-{index:D2}", EmbyItemType.Movie)).ToArray();
                return Task.FromResult(Json(new { Items = items, TotalRecordCount = total }));
            }
            return Task.FromResult(JellyfinResponse(request, poster));
        };
        (frame.Content as IShellContent)?.Release();
        frame.Navigate(typeof(LibraryPage), new LibraryRequest
        {
            Services = fixture.Services,
            Title = "离线索引留边",
            ParentId = "index-lane",
            CollectionType = "movies"
        });
        var page = (LibraryPage)frame.Content;
        await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
        var alpha = Get<AlphaBar>(page, "Alpha");
        var rail = Get<Border>(alpha, "Rail");
        var host = (Grid)alpha.Parent;
        foreach (var size in new[] { (Width: 1132, Height: 1084), (Width: 600, Height: 560), (Width: 1132, Height: 1084) })
        {
            ResizeProbeClient(size.Width, size.Height);
            await LayoutAsync(page);
            Require(rail.Visibility == (size.Height == 560 ? Visibility.Collapsed : Visibility.Visible),
                "字母条没有随高矮窗口切换实际显示状态");
            if (rail.Visibility == Visibility.Visible)
            {
                Require(host.ColumnDefinitions[1].ActualWidth > 0, "字母条可见但未占独立列");
                var railLeft = rail.TransformToVisual(page).TransformPoint(new(0, 0)).X;
                var cards = Descendants(Get<ItemsView>(page, "CardWall")).OfType<PosterCard>().ToArray();
                Require(cards.Length > 0 && cards.All(card => card.TransformToVisual(page).TransformPoint(new(card.ActualWidth, 0)).X < railLeft),
                    "Jellyfin最后一列卡片仍碰到字母条");
            }
            else Require(host.ColumnDefinitions[1].ActualWidth <= 1, "字母条按高度隐藏后仍占空间");
            await SaveProbeScreenAsync($"jellyfin-index-lane-{size.Width}-{size.Height}-screen.png");
        }
        total = 1;
        await page.ViewModel.ReloadAsync();
        await LayoutAsync(page);
        Require(alpha.Visibility == Visibility.Collapsed && host.ColumnDefinitions[1].ActualWidth <= 1,
            "少于31项后字母条未收起或仍占空间");
        total = 36;
        await page.ViewModel.ReloadAsync();
        await LayoutAsync(page);
        Require(alpha.Visibility == Visibility.Visible && host.ColumnDefinitions[1].ActualWidth > 0,
            "字母条重新可用后没有恢复独立空间");
    }

    private static void RequireResumeGap(LibraryPage page, double toolbarBottom) =>
        Require(Math.Abs(PosterTop(page) - toolbarBottom - 28) <= 2,
            "继续观看仍有隐藏行占位或不是28DIP间距");
}
