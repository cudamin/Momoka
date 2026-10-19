using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 2026-10-19 用户令「把飞牛库的间距也缩短」：飞牛分区库同样有收起行按 RowSpacing 占位的问题，
    /// 工具栏到封面空出 100 像素。这里量收起与展开筛选面板两种状态，确保空档只剩一档内容间距。
    /// </summary>
    private async Task FnNasLibrarySpacingAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync(400, 600);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            // 产品探测先试 Emby 的公开信息；飞牛地址必须让这两条落空，否则会被认成 Emby。
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.9.8" }));
            if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { server_guid = "fn-guid", server_name = "离线飞牛" }));
            if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { guid = "user", username = "user", is_admin = "1" }));
            if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new[] { new { guid = "fn-library", name = "飞牛电影", category = "Movie" } }));
            if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new
                {
                    list = new[]
                    {
                        new
                        {
                            guid = "fn-movie-1", title = "离线飞牛电影", type = "Movie",
                            poster = "poster.jpg", vote_average = 8.1,
                            media_stream = new { resolutions = new[] { "1080p" } }
                        }
                    },
                    total = 1
                }));
            if (path.EndsWith("/api/v1/tag/list", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { }));
            if (path.EndsWith("/api/v1/tag/genres", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/tag/iso3166", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(Array.Empty<object>()));
            if (path.Contains("/api/v1/sys/img/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        await fixture.SelectAsync(host: "fn.invalid", kind: MediaServerKind.FnNas);

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            var request = new LibraryRequest
            {
                Services = fixture.Services,
                Title = "飞牛电影",
                ParentId = "fn-library",
                CollectionType = "movies"
            };
            frame.Navigate(typeof(LibraryPage), request);
            var page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.IsFnNas, "探针没有落进飞牛媒体库页");
            Require(page.ViewModel.Cards.Count == 1, "飞牛媒体库首屏未载入");
            Require(Get<Border>(page, "LibrarySectionsSurface").Visibility == Visibility.Collapsed,
                "飞牛媒体库不该画「影片 / 收藏」分段");

            ResizeProbeClient(1280, 800);
            await LayoutAsync(page);
            var toolbar = Get<Grid>(page, "ToolbarSurface");
            var controls = toolbar.TransformToVisual(page).TransformBounds(new(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
            var panel = Get<Border>(page, "FnNasFilterPanel");
            Require(panel.Visibility == Visibility.Collapsed, "筛选面板默认应收起");
            var closedGap = PosterTop(page) - controls.Bottom;
            Require(closedGap is >= 8 and <= 24,
                $"收起筛选时封面离工具栏 {closedGap:0} 像素，期望 20 像素左右");
            await SaveProbeScreenAsync("library-fnnas-spacing-closed-screen.png");

            var invoke = new ButtonAutomationPeer(Get<Button>(page, "FilterButton"));
            ((IInvokeProvider)invoke.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => page.ViewModel.FnNasFilterOpen && !page.ViewModel.FnNasFilterLoading);
            await LayoutAsync(page);
            Require(panel.Visibility == Visibility.Visible, "筛选面板没有展开");
            var panelBox = panel.TransformToVisual(page).TransformBounds(new(0, 0, panel.ActualWidth, panel.ActualHeight));
            var panelGap = panelBox.Top - controls.Bottom;
            Require(panelGap is >= 12 and <= 20,
                $"筛选面板离工具栏 {panelGap:0} 像素，期望 16 像素左右");
            var openGap = PosterTop(page) - panelBox.Bottom;
            Require(openGap is >= 20 and <= 30,
                $"展开筛选后封面离面板 {openGap:0} 像素，期望 24 像素左右");
            await SaveProbeScreenAsync("library-fnnas-spacing-open-screen.png");
        }
        finally { frame.Content = null; }
    }

    private static double PosterTop(LibraryPage page) =>
        Descendants(Get<ItemsView>(page, "CardWall")).OfType<PosterCard>()
            .Select(card => card.TransformToVisual(page).TransformPoint(new(0, 0)).Y)
            .DefaultIfEmpty(double.NaN).Min();

    private static HttpResponseMessage FnNasEnvelope(object data) => Json(new { code = 0, data });
}
